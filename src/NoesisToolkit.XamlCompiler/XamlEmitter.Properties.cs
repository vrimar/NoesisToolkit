using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>Attributes: plain properties, attached properties, setters and the dependency
/// property references they resolve through.</summary>
sealed partial class XamlEmitter
{
    void ApplyAttribute(
        XElement element,
        string target,
        INamedTypeSymbol type,
        XAttribute attribute
    )
    {
        if (attribute.IsNamespaceDeclaration)
            return;

        var ns = attribute.Name.NamespaceName;
        var local = attribute.Name.LocalName;

        // Build-time only. Left alone, ntk:DataType would be applied as the real DataType.
        if (ns == XamlTypeResolver.ToolkitNs)
            return;

        if (ns == XamlTypeResolver.DirectiveNs)
        {
            switch (local)
            {
                case "Key":
                case "Uid":
                case "Class":
                case "ClassModifier":
                case "FieldModifier":
                    return;
                case "Name":
                    EmitName(target, attribute.Value, type);
                    return;
                case "Shared":
                    return;
                default:
                    Errors.Add($"x:{local} is not supported");
                    return;
            }
        }

        if (local.Contains("."))
        {
            ApplyAttachedAttribute(element, target, type, attribute.Value, local, ns);
            return;
        }

        if (local is "Property" && IsSetterLike(type))
        {
            ApplySetterProperty(element, target, attribute.Value);
            return;
        }

        if (local is "Value" && IsSetterLike(type))
        {
            ApplySetterValue(element, target, attribute.Value);
            return;
        }

        ApplyProperty(element, target, type, local, attribute.Value);
    }

    void EmitName(string target, string name, INamedTypeSymbol type)
    {
        if (resolver.FindProperty(type, "Name")?.SetMethod is not null)
            _lines.Add($"{target}.Name = {Quote(name)};");

        if (_templates.Count > 0)
        {
            _lines.Add(
                $"{_templates[_templates.Count - 1]}.RegisterName({Quote(name)}, {target});"
            );
            return;
        }

        // The field is what the generated code reads, but a binding that fell back to the engine
        // resolves ElementName through the name scope and nothing else populates it.
        if (_rootClass is not null)
        {
            _lines.Add($"this._{name} = {target};");
            _lines.Add($"{RootScope}.RegisterName({Quote(name)}, {target});");
        }
    }

    static bool IsSetterLike(INamedTypeSymbol type) =>
        type.Name is "Setter" or "Trigger" or "Condition" or "DataTrigger";

    /// <summary>The DependencyProperty a Setter/Trigger names, resolved against the styled type.</summary>
    /// <summary>Parses a markup extension, recording the failure the callers all reported the
    /// same way.</summary>
    bool TryMarkup(string raw, out MarkupCall call)
    {
        if (XamlMarkupParser.Parse(raw) is { } parsed)
        {
            call = parsed;
            return true;
        }

        call = null!;
        Errors.Add($"could not parse markup extension '{raw}'");
        return false;
    }

    void ApplySetterProperty(XElement element, string target, string raw)
    {
        var scopeName =
            element.Attribute("TargetName")?.Value ?? element.Attribute("SourceName")?.Value;
        var scoped = scopeName is null ? null : LookupName(scopeName);

        var reference = ResolveDependencyPropertyReference(element, raw, scoped, out var valueType);
        if (reference is null)
            return;

        _pendingValueTypes[target] = valueType;
        _lines.Add($"{target}.Property = {reference};");
    }

    readonly Dictionary<string, ITypeSymbol?> _pendingValueTypes = new Dictionary<
        string,
        ITypeSymbol?
    >(StringComparer.Ordinal);

    void ApplySetterValue(XElement element, string target, string raw)
    {
        _pendingValueTypes.TryGetValue(target, out var valueType);

        if (XamlMarkupParser.IsMarkup(raw))
        {
            if (!TryMarkup(raw, out var call))
                return;

            if (call.Name == "StaticResource" && _dictionaries.Count > 0)
            {
                var key = ResourceKeyExpression(element, call);
                if (key is not null)
                    AddGraphStatement(found => $"{target}.Value = {found};", key);
                return;
            }

            var expression = MarkupValue(element, call, valueType);
            if (expression is not null)
                _lines.Add($"{target}.Value = {expression};");
            return;
        }

        var text = XamlMarkupParser.Unescape(raw);
        var value = valueType is null
            ? Quote(text)
            : ConvertValue(element, text, valueType) ?? Quote(text);

        _lines.Add($"{target}.Value = {value};");
    }

    string? ResolveDependencyPropertyReference(
        XElement element,
        string raw,
        INamedTypeSymbol? scoped,
        out ITypeSymbol? valueType
    )
    {
        valueType = null;
        var name = raw.Trim();

        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            var ownerReference = name.Substring(0, dot);
            var propertyName = name.Substring(dot + 1);
            var owner = ResolveTypeSymbol(element, ownerReference);
            if (owner is null)
            {
                return Fail($"could not resolve Setter owner '{ownerReference}'");
            }

            var setter = resolver.FindAttachedSetter(owner, propertyName);
            valueType = setter?.Parameters[1].Type;

            if (setter is null)
            {
                var instance = resolver.FindProperty(owner, propertyName);
                valueType = instance?.Type;
            }

            return SlotReference(owner, propertyName);
        }

        var styled = scoped ?? StyleTarget;
        if (styled is null)
        {
            return Fail($"Setter Property='{name}' has no TargetType in scope");
        }

        var property = resolver.FindProperty(styled, name);
        if (property is null)
        {
            var attached = resolver.FindAttachedValueType(styled, name);
            if (attached is null)
            {
                return Fail($"'{name}' is not a property of {XamlTypeResolver.Fqn(styled)}");
            }

            valueType = attached;
            return SlotReference(styled, name);
        }

        valueType = property.Type;
        var declaring =
            resolver.FindDependencyPropertyOwner(styled, name) ?? property.ContainingType;
        return SlotReference(declaring, name);
    }

    void ApplyAttachedAttribute(
        XElement element,
        string target,
        INamedTypeSymbol type,
        string rawValue,
        string local,
        string ns
    )
    {
        var dot = local.IndexOf('.');
        var ownerName = local.Substring(0, dot);
        var propertyName = local.Substring(dot + 1);

        var owner = ResolveIn(element, ns, ownerName);
        if (owner is null)
        {
            Errors.Add($"could not resolve attached-property owner '{ownerName}'");
            return;
        }

        var ownerFqn = XamlTypeResolver.Fqn(owner);
        var valueType = resolver.FindAttachedValueType(owner, propertyName);

        // An attached event has no Set{Name} and must go through the loader. A missing setter is not
        // an error: another generator in this same pass may be the one that emits it.
        if (valueType is null && resolver.FindEvent(owner, propertyName) is not null)
            return;

        if (XamlMarkupParser.IsMarkup(rawValue))
        {
            if (!TryMarkup(rawValue, out var call))
                return;

            if (call.Name is "Binding" or "TemplateBinding")
            {
                if (
                    AttachedSlot(owner, propertyName) is { } slot
                    && TryEmitCompiledBinding(element, target, type, slot, call)
                )
                    return;

                var binding = EmitBindingLike(element, call);
                if (binding is not null)
                    _lines.Add(
                        $"{target}.SetBinding({ownerFqn}.{propertyName}Property, {binding});"
                    );
                return;
            }

            var expression = MarkupValue(element, call, valueType);
            if (expression is not null)
                _lines.Add($"{ownerFqn}.Set{propertyName}({target}, {expression});");
            return;
        }

        // The parser ignores an attached property the owner does not declare; emitting a setter for
        // one would be a compile error where the document merely had a typo.
        if (valueType is null && resolver.FindAttachedSetter(owner, propertyName) is null)
        {
            DeadMarkup.Add($"{ownerName}.{propertyName} is not an attached property");
            return;
        }

        var text = XamlMarkupParser.Unescape(rawValue);
        var value = valueType is null ? LiteralGuess(text) : ConvertValue(element, text, valueType);

        if (value is not null)
            _lines.Add($"{ownerFqn}.Set{propertyName}({target}, {value});");
    }

    void ApplyProperty(
        XElement element,
        string target,
        INamedTypeSymbol type,
        string name,
        string rawValue
    )
    {
        if (resolver.FindProperty(type, name) is null && resolver.FindEvent(type, name) is { })
        {
            if (_rootClass is null)
            {
                DeadMarkup.Add(
                    $"<{element.Name.LocalName} {name}=...> — an event handler needs an x:Class root"
                );
                return;
            }

            _lines.Add($"{target}.{name} += this.{rawValue.Trim()};");
            return;
        }

        var property = resolver.FindProperty(type, name);
        if (property is null)
        {
            // The native parser drops an unknown attribute in silence; say so and carry on.
            DeadMarkup.Add(
                $"<{element.Name.LocalName} {name}=...> — {XamlTypeResolver.Fqn(type)} has no such property"
            );
            return;
        }

        if (XamlMarkupParser.IsMarkup(rawValue))
        {
            if (!TryMarkup(rawValue, out var call))
                return;

            ApplyMarkup(element, target, type, property, call);
            return;
        }

        var value = ConvertValue(element, XamlMarkupParser.Unescape(rawValue), property.Type);
        if (value is null)
            return;

        if (property.SetMethod is null)
        {
            Errors.Add($"'{name}' on {XamlTypeResolver.Fqn(type)} is read-only");
            return;
        }

        _lines.Add($"{target}.{name} = {value};");
    }

    void ApplyMarkup(
        XElement element,
        string target,
        INamedTypeSymbol type,
        IPropertySymbol property,
        MarkupCall call
    )
    {
        var bindable = XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement");

        switch (call.Name)
        {
            case "Binding"
            or "TemplateBinding"
                when PlainSlot(property) is { } slot
                    && TryEmitCompiledBinding(element, target, type, slot, call):
                return;
            case "Binding":
            case "TemplateBinding":
            {
                var binding = EmitBindingLike(element, call);
                if (binding is null)
                    return;

                if (BindStatement(target, type, property, binding) is { } statement)
                    _lines.Add(statement);
                return;
            }
            case "StaticResource":
            case "DynamicResource":
            {
                var key = ResourceKeyExpression(element, call);
                if (key is null)
                    return;

                // A built-in theme style lives in the native theme, unreachable from managed resources.
                if (property.Name == "BasedOn")
                {
                    var probe = BasedOnProbe(element);
                    AddGraphStatement(
                        found => $"{target}.BasedOn = (global::Noesis.Style)({found} ?? {probe});",
                        key
                    );
                    return;
                }

                // Only a DynamicResource stays live: a reference tracks tree position and re-reads on
                // every resource change, which a StaticResource must never do.
                if (bindable && call.Name == "DynamicResource")
                {
                    if (DependencyPropertyRef(property) is { } slot)
                        _lines.Add($"{target}.SetResourceReference({slot}, {key});");

                    return;
                }

                if (property.SetMethod is null)
                {
                    Errors.Add($"'{property.Name}' on {XamlTypeResolver.Fqn(type)} is read-only");
                    return;
                }

                var cast = $"({XamlTypeResolver.Fqn(property.Type)})";
                AddGraphStatement(found => $"{target}.{property.Name} = {cast}{found};", key);
                return;
            }
            default:
            {
                var expression = MarkupValue(element, call, property.Type);
                if (expression is not null)
                    _lines.Add($"{target}.{property.Name} = {expression};");
                return;
            }
        }
    }

    /// <summary>SetBinding is a FrameworkElement member; any other DependencyObject has to go
    /// through BindingOperations, and a BindingBase-typed slot stores the binding itself.</summary>
    string? BindStatement(
        string target,
        INamedTypeSymbol type,
        IPropertySymbol property,
        string binding
    )
    {
        if (XamlTypeResolver.DerivesFrom(property.Type, "global::Noesis.BindingBase"))
            return $"{target}.{property.Name} = {binding};";

        if (XamlTypeResolver.DerivesFrom(type, "global::Noesis.DependencyObject"))
        {
            if (DependencyPropertyRef(property) is not { } slot)
                return null;

            return XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement")
                ? $"{target}.SetBinding({slot}, {binding});"
                : $"global::Noesis.BindingOperations.SetBinding({target}, {slot}, {binding});";
        }

        return $"{target}.{property.Name} = {binding};";
    }

    string? DependencyPropertyRef(IPropertySymbol property) =>
        resolver.FindDependencyPropertyOwner(property.ContainingType, property.Name) is { } owner
            ? SlotReference(owner, property.Name)
            : Fail(
                $"'{property.Name}' on {XamlTypeResolver.Fqn(property.ContainingType)} "
                    + "is not a dependency property, so it cannot take a binding"
            );
}
