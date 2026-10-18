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
        var outer = _attribute;
        _attribute = attribute;
        try
        {
            ApplyAttributeCore(element, target, type, attribute);
        }
        finally
        {
            _attribute = outer;
        }
    }

    void ApplyAttributeCore(
        XElement element,
        string target,
        INamedTypeSymbol type,
        XAttribute attribute
    )
    {
        if (attribute.IsNamespaceDeclaration || IsIgnorable(element, attribute))
            return;

        if (IsTemplateBinding(attribute))
        {
            if (_grafted.Contains(element))
                Tally.NativeByDesign++;
            else
                DeadMarkup.Add(
                    $"<{element.Name.LocalName} {attribute.Name.LocalName}=...> — a {{TemplateBinding}} the parser cannot build here is left unset"
                );
            return;
        }

        var ns = attribute.Name.NamespaceName;
        var local = attribute.Name.LocalName;

        // Build-time only. Left alone, ntk:DataType would be applied as the real DataType.
        if (ns == XamlTypeResolver.ToolkitNs || ns == XNamespace.Xml.NamespaceName)
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

        if (ns.Length > 0 && ns != element.Name.NamespaceName)
        {
            DeadMarkup.Add(
                $"<{element.Name.LocalName} {attribute.Name}=...> — an attribute in another xmlns is not a property"
            );
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

        // The parser registers a plain Name as it does x:Name, only with no field behind it.
        if (
            local == "Name"
            && ns.Length == 0
            && element.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs)) is null
            && XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement")
        )
        {
            EmitName(target, attribute.Value.Trim(), type, field: false);
            return;
        }

        ApplyProperty(element, target, type, local, attribute.Value);
    }

    void EmitName(string target, string name, INamedTypeSymbol type, bool field = true)
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
            if (field)
                _lines.Add($"this.{XamlCodeBehind.Field(name)} = {target};");
            _lines.Add($"{RootScope}.RegisterName({Quote(name)}, {target});");
        }
    }

    internal static bool IsIgnorable(XElement element, XAttribute attribute) =>
        IsIgnorable(element, attribute.Name.NamespaceName);

    internal static bool IsIgnorable(XElement element) =>
        element.Parent is { } parent && IsIgnorable(parent, element.Name.NamespaceName);

    static bool IsIgnorable(XElement element, string ns)
    {
        if (ns.Length == 0)
            return false;

        if (ns == XamlTypeResolver.CompatibilityNs)
            return true;

        foreach (var scope in element.AncestorsAndSelf())
        {
            if (
                scope.Attribute(XName.Get("Ignorable", XamlTypeResolver.CompatibilityNs))
                is not { } ignorable
            )
                continue;

            foreach (
                var prefix in ignorable.Value.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                if (scope.GetNamespaceOfPrefix(prefix)?.NamespaceName == ns)
                    return true;
            }
        }

        return false;
    }

    static bool IsSetterLike(INamedTypeSymbol type) =>
        XamlTypeResolver.Fqn(type)
            is "global::Noesis.Setter"
                or "global::Noesis.Trigger"
                or "global::Noesis.Condition"
                or "global::Noesis.DataTrigger";

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

    // Element text is never markup and keeps its braces: only an attribute value escapes.
    void ApplySetterValue(XElement element, string target, string raw, bool text = false)
    {
        _pendingValueTypes.TryGetValue(target, out var valueType);

        if (!text && XamlMarkupParser.IsMarkup(raw))
        {
            if (!TryMarkup(raw, out var call))
                return;

            if (
                call.Name == "StaticResource"
                && (_dictionaries.Count > 0 || _rootClass is not null)
            )
            {
                var key = ResourceKeyExpression(element, call);
                if (key is not null)
                    AddGraphStatement(found => $"{target}.Value = {found};", key);
                return;
            }

            var expression = IsCustomExtension(call)
                ? ProvidedValue(element, call, target, "null")
                : MarkupValue(element, call, valueType);
            if (expression is not null)
                _lines.Add($"{target}.Value = {expression};");
            return;
        }

        var literal = text ? raw : XamlMarkupParser.Unescape(raw);
        var setter = BindingHost(element);
        if (setter.Attribute("Property")?.Value.Trim() is { } property)
            RootSetterValue(setter, property, literal);

        var value = valueType is null
            ? Quote(literal)
            : ConvertValue(element, literal, valueType) ?? Quote(literal);

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

            if (resolver.FindDependencyPropertyOwner(owner, propertyName) is not { } slotOwner)
            {
                return Fail(
                    $"'{name}' names no dependency property of {XamlTypeResolver.Fqn(owner)}"
                );
            }

            return SlotReference(slotOwner, propertyName);
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
            // The parser drops an attribute in an xmlns it knows nothing of.
            if (ns.Length > 0 && !resolver.Maps(ns))
                DeadMarkup.Add($"<{element.Name.LocalName} {local}=...> is in an unknown xmlns");
            else
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

            if (
                valueType is null
                && resolver.FindAttachedSetter(owner, propertyName) is null
                && resolver.FindDependencyPropertyOwner(owner, propertyName) is null
            )
            {
                DeadMarkup.Add($"{ownerName}.{propertyName} is not an attached property");
                return;
            }

            if (call.Name is "Binding")
            {
                if (
                    AttachedSlot(owner, propertyName) is { } slot
                    && TryEmitCompiledBinding(element, target, type, slot, call)
                )
                    return;

                var binding = EmitBindingLike(element, call, propertyName);
                if (binding is not null)
                    _lines.Add(
                        SlotBindStatement(
                            target,
                            type,
                            $"{ownerFqn}.{propertyName}Property",
                            binding
                        )
                    );
                return;
            }

            if (call.Name is "StaticResource" or "DynamicResource")
            {
                ApplyAttachedResource(element, target, type, owner, propertyName, valueType, call);
                return;
            }

            if (IsCustomExtension(call))
            {
                var slot = resolver.FindDependencyPropertyOwner(owner, propertyName) is not null
                    ? $"{ownerFqn}.{propertyName}Property"
                    : null;
                ApplyProvided(
                    element,
                    call,
                    target,
                    slot,
                    slot is null
                        ? null
                        : provided => SlotBindStatement(target, type, slot, provided),
                    provided =>
                        $"{ownerFqn}.Set{propertyName}({target}, {CastPrefix(valueType)}{provided});"
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
        var value = valueType is null
            ? LiteralGuess(text)
            : ConvertOrFail(element, text, valueType, local);

        if (value is not null)
            _lines.Add($"{ownerFqn}.Set{propertyName}({target}, {value});");
    }

    void ApplyAttachedResource(
        XElement element,
        string target,
        INamedTypeSymbol type,
        INamedTypeSymbol owner,
        string propertyName,
        ITypeSymbol? valueType,
        MarkupCall call
    )
    {
        var key = ResourceKeyExpression(element, call);
        if (key is null)
            return;

        if (
            call.Name == "DynamicResource"
            && XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement")
        )
        {
            if (resolver.FindDependencyPropertyOwner(owner, propertyName) is not { } declaring)
            {
                Errors.Add(
                    $"'{propertyName}' on {XamlTypeResolver.Fqn(owner)} is not a dependency property, "
                        + "so it cannot take a DynamicResource"
                );
                return;
            }

            _lines.Add(
                $"{target}.SetResourceReference({SlotReference(declaring, propertyName)}, {key});"
            );
            return;
        }

        var ownerFqn = XamlTypeResolver.Fqn(owner);
        AddGraphStatement(
            Assigning(valueType, value => $"{ownerFqn}.Set{propertyName}({target}, {value});"),
            key
        );
    }

    // A resource of another type leaves the slot alone, as the parser leaves it, where an unbox throws.
    Func<string, string> Assigning(ITypeSymbol? type, Func<string, string> assign)
    {
        if (type is null || TakesAnything(type))
            return assign;

        if (!type.IsValueType)
            return found => assign($"({XamlTypeResolver.Fqn(type)}){found}");

        var held = NextName("held");
        var unboxed = XamlTypeResolver.Fqn(Wrapped(type) ?? type);
        return found => $"if ({found} is {unboxed} {held}) {{ {assign(held)} }}";
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

        RootNamedByText(element, type, name, XamlMarkupParser.Unescape(rawValue));

        var value = ConvertOrFail(
            element,
            XamlMarkupParser.Unescape(rawValue),
            property.Type,
            name
        );
        if (value is null)
            return;

        if (!Settable(property, type))
        {
            Errors.Add($"'{name}' on {XamlTypeResolver.Fqn(type)} is read-only");
            return;
        }

        _lines.Add($"{Member(target, name)} = {value};");
    }

    bool Settable(IPropertySymbol property, ITypeSymbol receiver) =>
        property.SetMethod is { } setter && resolver.IsAccessible(setter, _rootClass, receiver);

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
                when PlainSlot(property) is { } slot
                    && TryEmitCompiledBinding(element, target, type, slot, call):
                return;
            case "Binding":
            {
                var binding = EmitBindingLike(element, call, property.Name);
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

                if (!Settable(property, type))
                {
                    Errors.Add($"'{property.Name}' on {XamlTypeResolver.Fqn(type)} is read-only");
                    return;
                }

                AddGraphStatement(
                    Assigning(
                        property.Type,
                        value => $"{Member(target, property.Name)} = {value};"
                    ),
                    key,
                    skipMissing: true
                );
                return;
            }
            default:
            {
                if (!Settable(property, type))
                {
                    Errors.Add($"'{property.Name}' on {XamlTypeResolver.Fqn(type)} is read-only");
                    return;
                }

                if (IsCustomExtension(call))
                {
                    var slot =
                        XamlTypeResolver.DerivesFrom(type, "global::Noesis.DependencyObject")
                        && resolver.FindDependencyPropertyOwner(
                            property.ContainingType,
                            property.Name
                        )
                            is { } owner
                            ? SlotReference(owner, property.Name)
                            : null;
                    ApplyProvided(
                        element,
                        call,
                        target,
                        slot,
                        slot is null
                            ? null
                            : provided => BindStatement(target, type, property, provided)!,
                        provided =>
                            $"{Member(target, property.Name)} = {CastPrefix(property.Type)}{provided};"
                    );
                    return;
                }

                var expression = MarkupValue(element, call, property.Type);
                if (expression is not null)
                    _lines.Add($"{Member(target, property.Name)} = {expression};");
                return;
            }
        }
    }

    void ApplyProvided(
        XElement element,
        MarkupCall call,
        string target,
        string? slot,
        Func<string, string>? bind,
        Func<string, string> assign
    )
    {
        if (ProvidedValue(element, call, target, slot ?? "null") is not { } provided)
            return;

        var value = NextName("provided");
        _lines.Add($"var {value} = {provided};");

        if (bind is null)
        {
            _lines.Add(assign(value));
            return;
        }

        var bound = NextName("bound");
        _lines.Add(
            $"if ({value} is global::Noesis.BindingBase {bound}) {bind(bound)} else {assign(value)}"
        );
    }

    static string SlotBindStatement(
        string target,
        INamedTypeSymbol type,
        string slot,
        string binding
    ) =>
        XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement")
            ? $"{target}.SetBinding({slot}, {binding});"
            : $"global::Noesis.BindingOperations.SetBinding({target}, {slot}, {binding});";

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
            return $"{Member(target, property.Name)} = {binding};";

        if (XamlTypeResolver.DerivesFrom(type, "global::Noesis.DependencyObject"))
        {
            if (DependencyPropertyRef(property) is not { } slot)
                return null;

            return XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement")
                ? $"{target}.SetBinding({slot}, {binding});"
                : $"global::Noesis.BindingOperations.SetBinding({target}, {slot}, {binding});";
        }

        return $"{Member(target, property.Name)} = {binding};";
    }

    string? DependencyPropertyRef(IPropertySymbol property) =>
        resolver.FindDependencyPropertyOwner(property.ContainingType, property.Name) is { } owner
            ? SlotReference(owner, property.Name)
            : Fail(
                $"'{property.Name}' on {XamlTypeResolver.Fqn(property.ContainingType)} "
                    + "is not a dependency property, so it cannot take a binding"
            );
}
