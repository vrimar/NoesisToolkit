using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>Markup extensions in value position — bindings, converters and the rest.</summary>
sealed partial class XamlEmitter
{
    /// <summary>An expression for a markup extension used in value position.</summary>
    string? MarkupValue(XElement element, MarkupCall call, ITypeSymbol? targetType)
    {
        switch (call.Name)
        {
            case "x:Null":
            case "Null":
                return "null";

            case "x:Type":
            case "Type":
            {
                var type = ResolveTypeReference(element, call.Positional.FirstOrDefault() ?? "");
                if (type is null)
                {
                    return Fail("could not resolve {x:Type}");
                }

                return $"typeof({type})";
            }

            case "x:Static":
            case "Static":
            {
                var reference = call.Positional.FirstOrDefault() ?? "";
                var dot = reference.LastIndexOf('.');
                if (dot <= 0)
                {
                    return Fail($"malformed {{x:Static {reference}}}");
                }

                var owner = ResolveTypeSymbol(element, reference.Substring(0, dot));
                if (owner is null)
                {
                    return Fail($"could not resolve {{x:Static {reference}}}");
                }

                var member = reference.Substring(dot + 1);
                if (!XamlMarkup.IsIdentifier(member) || owner.GetMembers(member).IsEmpty)
                {
                    return Fail($"{{x:Static {reference}}} names no member");
                }

                return $"{XamlTypeResolver.Fqn(owner)}.{member}";
            }

            case "StaticResource":
            case "DynamicResource":
            {
                var key = ResourceKeyExpression(element, call);
                return key is null ? null : $"new global::Noesis.DynamicResourceExtension({key})";
            }

            case "Binding":
            case "TemplateBinding":
                return EmitBindingLike(element, call);

            default:
                return EmitCustomExtension(element, call, targetType);
        }
    }

    string? EmitCustomExtension(XElement element, MarkupCall call, ITypeSymbol? targetType)
    {
        var symbol =
            ResolveTypeSymbol(element, call.Name)
            ?? ResolveTypeSymbol(element, call.Name + "Extension");

        if (symbol is null)
        {
            return Fail($"markup extension '{{{call.Name}}}' does not resolve to a type");
        }

        if (!XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.MarkupExtension"))
        {
            return Fail($"'{call.Name}' is not a MarkupExtension");
        }

        var name = NextName(symbol.Name);
        _lines.Add($"var {name} = new {XamlTypeResolver.Fqn(symbol)}();");

        var content = resolver.FindContentProperty(symbol);

        if (call.Positional.Count > 1 || call.PositionalCalls.Count > 0)
        {
            return Fail($"'{call.Name}' takes at most one plain positional argument");
        }

        for (var i = 0; i < call.Positional.Count; i++)
        {
            if (content is null)
            {
                return Fail($"'{call.Name}' takes no positional argument");
            }

            var property = resolver.FindProperty(symbol, content);
            if (property is null)
            {
                return Fail($"'{call.Name}' has no property '{content}'");
            }

            var value = ConvertValue(element, call.Positional[i], property.Type);
            if (value is null)
                return null;

            _lines.Add($"{name}.{content} = {value};");
        }

        foreach (var pair in call.Named)
        {
            var property = resolver.FindProperty(symbol, pair.Key);
            if (property is null)
            {
                return Fail($"'{call.Name}' has no property '{pair.Key}'");
            }

            if (pair.Value is not string text)
            {
                return Fail($"'{call.Name}.{pair.Key}' takes a nested extension");
            }

            var value = ConvertValue(element, text, property.Type);
            if (value is null)
                return null;

            _lines.Add($"{name}.{pair.Key} = {value};");
        }

        var cast = CastPrefix(targetType);

        return $"{cast}{name}.ProvideValue(null)";
    }

    /// <summary>An enum knob's member name. Nested markup has no name and would emit an
    /// expression that does not compile.</summary>
    string? EnumMember(KeyValuePair<string, object> pair) => EnumMember(pair.Key, Text(pair.Value));

    string? EnumMember(string knob, string raw) => EnumMember(knob, raw, null);

    string? EnumMember(string knob, string raw, string? enumName)
    {
        var text = raw.Trim();
        if (!XamlMarkup.IsIdentifier(text))
            return Fail($"Binding.{knob} must name a constant, not '{text}'");

        if (enumName is null)
            return text;

        var member = resolver
            .Resolve(XamlTypeResolver.PresentationNs, enumName)
            ?.GetMembers()
            .FirstOrDefault(m => m.Kind == SymbolKind.Field && m.Name == text);

        return member is not null ? text : Fail($"'{text}' is not a member of {enumName}");
    }

    string? EmitBindingLike(XElement element, MarkupCall call)
    {
        if (call.Name == "TemplateBinding")
        {
            DeadMarkup.Add("a {TemplateBinding} that is not an element's attribute is left unset");
            return null;
        }

        return EmitBinding(element, call);
    }

    static readonly System.Text.RegularExpressions.Regex PrefixedPathOwner = new(
        @"\((\w+):(\w+)\.",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    // Outside the parser no xmlns is in scope, so the engine can only find an owner by its type name.
    string NativePath(XElement scope, string path) =>
        PrefixedPathOwner.Replace(
            path,
            match =>
                ResolveTypeSymbol(scope, match.Groups[1].Value + ":" + match.Groups[2].Value)
                    is { IsGenericType: false } owner
                    ? "(" + EngineTypeName(owner) + "."
                    : match.Value
        );

    static string EngineTypeName(INamedTypeSymbol type) =>
        type.ContainingAssembly?.Name == "Noesis.GUI" ? type.Name
        : type.ContainingType is { } outer ? EngineTypeName(outer) + "+" + type.Name
        : type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString() + "." + type.Name
        : type.Name;

    string? EmitBinding(XElement element, MarkupCall call)
    {
        var name = NextName("binding");
        ClassifyFallback();
        var path = call.Positional.FirstOrDefault() ?? NamedValue(call, "Path") as string;

        _lines.Add(
            path is null
                ? $"var {name} = new global::Noesis.Binding();"
                : $"var {name} = new global::Noesis.Binding({Quote(NativePath(element, path))});"
        );

        foreach (var pair in call.Named)
        {
            switch (pair.Key)
            {
                case "Path":
                    continue;
                case "Mode":
                    if (EnumMember(pair.Key, Text(pair.Value), "BindingMode") is not { } mode)
                        return null;

                    _lines.Add($"{name}.Mode = global::Noesis.BindingMode.{mode};");
                    continue;
                case "UpdateSourceTrigger":
                    if (
                        EnumMember(pair.Key, Text(pair.Value), "UpdateSourceTrigger")
                        is not { } trigger
                    )
                        return null;

                    _lines.Add(
                        $"{name}.UpdateSourceTrigger = global::Noesis.UpdateSourceTrigger.{trigger};"
                    );
                    continue;
                case "StringFormat":
                    _lines.Add($"{name}.StringFormat = {Quote(Text(pair.Value))};");
                    continue;
                case "ElementName":
                    _lines.Add($"{name}.ElementName = {Quote(Text(pair.Value))};");
                    continue;
                case "Delay":
                {
                    var delay = Text(pair.Value).Trim();
                    if (!int.TryParse(delay, NumberStyles.Integer, Invariant, out _))
                        return Fail($"Binding.Delay must be an integer, not '{delay}'");

                    _lines.Add($"{name}.Delay = {delay};");
                    continue;
                }
                case "FallbackValue":
                case "TargetNullValue":
                case "ConverterParameter":
                case "Source":
                {
                    if (!EmitKnob(element, name, "Binding", pair.Key, pair.Value))
                        return null;

                    continue;
                }
                case "RelativeSource":
                {
                    if (pair.Value is not MarkupCall nested)
                    {
                        return Fail("RelativeSource must be a markup extension");
                    }

                    var relative = EmitRelativeSource(element, nested);
                    if (relative is null)
                        return null;

                    _lines.Add($"{name}.RelativeSource = {relative};");
                    continue;
                }
                case "Converter":
                {
                    if (!EmitConverter(element, pair.Value, "global::Noesis.IValueConverter", name))
                        return null;

                    continue;
                }
                default:
                    return Fail($"Binding.{pair.Key} is not supported");
            }
        }

        return name;
    }

    bool EmitKnob(XElement element, string binding, string owner, string knob, object value)
    {
        var text = knob == "StringFormat";

        switch (value)
        {
            // The parser refuses to store a resource reference on a binding, so the knob stays unset.
            case MarkupCall { Name: "DynamicResource" }:
                DeadMarkup.Add($"{owner}.{knob} cannot take a {{DynamicResource}}");
                return true;
            case MarkupCall { Name: "StaticResource" } resource:
            {
                var key = ResourceKeyExpression(element, resource);
                if (key is null)
                    return false;

                var found = ResourceValueExpression(key);
                _lines.Add($"{binding}.{knob} = {(text ? $"{found} as string" : found)};");
                return true;
            }
            case MarkupCall nested:
            {
                var expression = MarkupValue(element, nested, null);
                if (expression is null)
                    return false;

                _lines.Add($"{binding}.{knob} = {expression};");
                return true;
            }
            default:
                _lines.Add($"{binding}.{knob} = {Quote(Text(value))};");
                return true;
        }
    }

    /// <summary>A Binding seals on first use, well before the graph settles, so a converter cannot
    /// wait for it: the lookup runs inline and searches the enclosing dictionaries before the
    /// application graph a root can already see.</summary>
    bool EmitConverter(XElement element, object value, string interfaceFqn, string target)
    {
        switch (value)
        {
            case MarkupCall { Name: "StaticResource" or "DynamicResource" } resource:
            {
                var key = ResourceKeyExpression(element, resource);
                if (key is null)
                    return false;

                _lines.Add($"{target}.Converter = ({interfaceFqn}){ResourceValueExpression(key)};");
                return true;
            }
            case MarkupCall other:
            {
                var expression = MarkupValue(element, other, null);
                if (expression is null)
                    return false;

                _lines.Add($"{target}.Converter = ({interfaceFqn}){expression};");
                return true;
            }
            default:
                Errors.Add("a converter must be a markup extension");
                return false;
        }
    }

    string? EmitRelativeSource(XElement element, MarkupCall call)
    {
        var (mode, ancestor, levelText) = RelativeSourceParts(call);
        if (!int.TryParse(levelText, out var level) || level < 1)
            return Fail($"AncestorLevel must be a positive integer, not '{levelText}'");

        if (ancestor is not null)
        {
            var type = ResolveTypeReference(element, ancestor);
            if (type is null)
            {
                return Fail($"could not resolve AncestorType '{ancestor}'");
            }

            return $"new global::Noesis.RelativeSource(global::Noesis.RelativeSourceMode.FindAncestor, typeof({type}), {level})";
        }

        return mode switch
        {
            "Self" => "global::Noesis.RelativeSource.Self",
            "TemplatedParent" => "global::Noesis.RelativeSource.TemplatedParent",
            "PreviousData" =>
                "new global::Noesis.RelativeSource(global::Noesis.RelativeSourceMode.PreviousData)",
            "FindAncestor" =>
                "new global::Noesis.RelativeSource(global::Noesis.RelativeSourceMode.FindAncestor)",
            _ => Fail($"RelativeSource mode '{mode}' is not supported"),
        };
    }

    string? EmitMultiBinding(XElement element)
    {
        // Each child is emitted through the ordinary native route, so without this the reason they
        // fell back would read as nothing at all rather than as the multi-binding around them.
        _inMultiBinding++;
        try
        {
            return EmitMultiBindingCore(element);
        }
        finally
        {
            _inMultiBinding--;
            if (_inMultiBinding == 0)
                _refusal = null;
        }
    }

    int _inMultiBinding;

    string? EmitMultiBindingCore(XElement element)
    {
        var name = NextName("multi");
        _lines.Add($"var {name} = new global::Noesis.MultiBinding();");

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            if (attribute.Name.LocalName == "Converter")
            {
                var call = XamlMarkupParser.Parse(attribute.Value);
                if (call is null)
                {
                    return Fail($"could not parse '{attribute.Value}'");
                }

                if (!EmitConverter(element, call, "global::Noesis.IMultiValueConverter", name))
                    return null;

                continue;
            }

            if (attribute.Name.LocalName == "Mode")
            {
                if (EnumMember("Mode", attribute.Value, "BindingMode") is not { } mode)
                    return null;

                _lines.Add($"{name}.Mode = global::Noesis.BindingMode.{mode};");
                continue;
            }

            if (attribute.Name.LocalName == "UpdateSourceTrigger")
            {
                if (
                    EnumMember("UpdateSourceTrigger", attribute.Value, "UpdateSourceTrigger")
                    is not { } trigger
                )
                    return null;

                _lines.Add(
                    $"{name}.UpdateSourceTrigger = global::Noesis.UpdateSourceTrigger.{trigger};"
                );
                continue;
            }

            if (
                attribute.Name.LocalName
                is "StringFormat"
                    or "ConverterParameter"
                    or "FallbackValue"
                    or "TargetNullValue"
            )
            {
                object value = XamlMarkupParser.Unescape(attribute.Value);
                if (XamlMarkupParser.IsMarkup(attribute.Value))
                {
                    if (XamlMarkupParser.Parse(attribute.Value) is not { } call)
                        return Fail($"could not parse '{attribute.Value}'");

                    value = call;
                }

                if (!EmitKnob(element, name, "MultiBinding", attribute.Name.LocalName, value))
                    return null;

                continue;
            }

            return Fail($"MultiBinding.{attribute.Name.LocalName} is not supported");
        }

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "MultiBinding.Converter")
            {
                var value = child.Elements().FirstOrDefault();
                if (value is null)
                {
                    return Fail("<MultiBinding.Converter> is empty");
                }

                var instance = EmitObject(value);
                if (instance is null)
                    return null;

                _lines.Add($"{name}.Converter = (global::Noesis.IMultiValueConverter){instance};");
                continue;
            }

            if (child.Name.LocalName != "Binding")
            {
                return Fail($"<{child.Name.LocalName}> inside a MultiBinding is not supported");
            }

            var inner = EmitBindingElement(child);
            if (inner is null)
                return null;

            _lines.Add($"{name}.Bindings.Add({inner});");
        }

        return name;
    }

    string? EmitBindingElement(XElement element)
    {
        var call = BindingElementCall(element);
        if (call is null)
        {
            return Fail($"could not parse an attribute of <{element.Name.LocalName}>");
        }

        return EmitBinding(element, call);
    }

    static MarkupCall? BindingElementCall(XElement element)
    {
        var call = new MarkupCall { Name = "Binding" };

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            object value = attribute.Value;
            if (XamlMarkupParser.IsMarkup(attribute.Value))
            {
                var nested = XamlMarkupParser.Parse(attribute.Value);
                if (nested is null)
                    return null;

                value = nested;
            }

            call.Named.Add(new KeyValuePair<string, object>(attribute.Name.LocalName, value));
        }

        return call;
    }
}
