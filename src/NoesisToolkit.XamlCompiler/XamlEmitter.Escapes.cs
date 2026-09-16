using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>The handful of constructs the compiler hands back to the XAML parser, and the
/// pieces held back from those fragments because a parser running alone cannot resolve them.</summary>
sealed partial class XamlEmitter
{
    static readonly string[] LiveValueExtensions =
    {
        "Binding",
        "MultiBinding",
        "TemplateBinding",
        "DynamicResource",
    };

    static bool NeedsParsedSetter(XElement element, INamedTypeSymbol type)
    {
        if (type.Name != "Setter")
            return false;

        var valueElements = element
            .Elements()
            .Where(e => e.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal))
            .ToList();

        // Only the Setter's own value decides; a whole template below it is not the parser's to take.
        if (valueElements.Count > 0)
            return valueElements
                .SelectMany(e => e.Elements())
                .Any(e => e.Name.LocalName is "Binding" or "MultiBinding");

        var value = element.Attribute("Value")?.Value;
        if (value is null || !XamlMarkupParser.IsMarkup(value))
            return false;

        var call = XamlMarkupParser.Parse(value);
        return call is not null && LiveValueExtensions.Contains(call.Name);
    }

    /// <summary>Re-parses one Setter inside a probe Style, so its Property resolves and its
    /// value becomes the expression the parser would have produced.</summary>
    string? EmitSetterProbe(XElement setter)
    {
        var declared = setter
            .AncestorsAndSelf()
            .Select(e => e.Attribute("TargetType"))
            .FirstOrDefault(a => a is not null)
            ?.Value;

        var scopeNamespace = (string?)null;
        var attached = setter.Attribute("Property")?.Value.Contains('.') == true;
        var named = setter.Attribute("TargetName")?.Value;

        // A named target resolves Property against its own type, not the template's.
        if (!attached && named is not null && LookupNamed(named) is { } target)
        {
            var ns =
                target.Name.NamespaceName.Length == 0
                    ? DefaultNamespace(target)
                    : target.Name.NamespaceName;

            // Rebinding a namespace the fragment already maps would re-prefix its own elements.
            string prefix;
            if (ns == DefaultNamespace(setter))
                prefix = "";
            else if (setter.GetPrefixOfNamespace(ns) is { } bound)
                prefix = bound + ":";
            else
            {
                prefix = "__probe:";
                scopeNamespace = ns;
            }

            declared = "{x:Type " + prefix + target.Name.LocalName + "}";
        }
        else if (declared is null && !attached)
        {
            return Fail("a Setter with a live value needs a TargetType or TargetName in scope");
        }

        var style = StyleProbe(declared, new XElement(setter));
        var probe = Probe(setter, style);

        if (scopeNamespace is not null)
            probe.Add(new XAttribute(XNamespace.Xmlns + "__probe", scopeNamespace));

        // Noesis seals the binding as it parses, so the fragment has to carry what it names.
        foreach (var key in ResourceKeysIn(style))
        {
            if (DeclarationOf(setter, key) is not { } declaration)
            {
                return Fail(
                    $"a Setter left to the parser names '{key}', which its own file does not declare"
                );
            }

            probe.AddFirst(new XElement(declaration));
        }

        var name = NextName("setter");
        _lines.Add($"var {name} = (global::Noesis.Setter){ParsedStyle(probe)}.Setters[0];");

        return name;
    }

    static readonly System.Text.RegularExpressions.Regex StaticResourceKey = new(
        @"\{\s*StaticResource\s+([A-Za-z0-9_.]+)\s*\}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    static IEnumerable<string> ResourceKeysIn(XElement fragment) =>
        fragment
            .DescendantsAndSelf()
            .SelectMany(e => e.Attributes())
            .Where(a => !a.IsNamespaceDeclaration)
            .SelectMany(a =>
                StaticResourceKey.Matches(a.Value).Cast<System.Text.RegularExpressions.Match>()
            )
            .Select(m => m.Groups[1].Value)
            .Distinct();

    static XElement? DeclarationOf(XElement scope, string key) =>
        DeclaredIn(scope.Document?.Root, key);

    static XElement? DeclaredIn(XElement? root, string key) =>
        root
            ?.DescendantsAndSelf()
            .FirstOrDefault(e =>
                e.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs))?.Value == key
            );

    /// <summary>Noesis types a few slots as BindingExpressionBase, which no managed value can be
    /// assigned to; that element has to stay on the XAML parser.</summary>
    bool HasUnassignableSlot(XElement element, INamedTypeSymbol type) =>
        Unassignable(element, type, resolver);

    internal static bool Unassignable(
        XElement element,
        INamedTypeSymbol type,
        XamlTypeResolver resolver
    )
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName.Contains("."))
                continue;

            var property = resolver.FindProperty(type, attribute.Name.LocalName);
            if (
                property is not null
                && XamlTypeResolver.Fqn(property.Type)
                    is "global::Noesis.BindingExpressionBase"
                        or "global::Noesis.PathFigureCollection"
            )
                return true;
        }

        return false;
    }

    // The managed API cannot install a template-binding expression, so the parser builds each element
    // that carries one from those attributes alone, in a template of the same kind.
    void PlanTemplateBindings(XElement scope, XElement? template)
    {
        var carried = new List<XElement>();
        foreach (var element in OwnContent(scope))
        {
            if (TemplateBindingsOf(element).Any() && Graftable(element))
                carried.Add(element);
        }

        if (carried.Count == 0)
            return;

        var carriers = new XElement(XName.Get("Grid", XamlTypeResolver.PresentationNs));
        foreach (var element in carried)
        {
            var shell = new XElement(
                element.Name,
                TemplateBindingsOf(element).Select(a => new XAttribute(a))
            );
            if (TextConstructorOf(element, resolver.SymbolOf(element)!) != TextConstructor.None)
                shell.Add(TextOf(element));

            CopyNamespaces(shell, element);
            carriers.Add(
                new XElement(
                    XName.Get("ContentControl", XamlTypeResolver.PresentationNs),
                    new XElement(
                        XName.Get("ContentControl.Tag", XamlTypeResolver.PresentationNs),
                        shell
                    )
                )
            );
        }

        var probe = template is null
            ? carriers
            : new XElement(template.Name, TargetTypeOf(template), carriers);
        CopyNamespaces(probe, scope);

        var parts = NextName("templated");
        _lines.Add(
            $"var {parts} = __templated(global::Noesis.GUI.ParseXaml({Verbatim(probe.ToString())}), {carried.Count});"
        );
        _usesTemplated = true;

        for (var i = 0; i < carried.Count; i++)
            _grafts[carried[i]] = $"{parts}[{i}]";
    }

    readonly Dictionary<XElement, string> _grafts = new Dictionary<XElement, string>();

    readonly HashSet<XElement> _grafted = new HashSet<XElement>();

    bool _usesTemplated;

    const string TemplatedHelper =
        "static object[] __templated(object parsed, int count)\n"
        + "{\n"
        + "    var carriers = (global::Noesis.Panel)(parsed is global::Noesis.FrameworkTemplate t ? t.VisualTree : parsed);\n"
        + "    var parts = new object[count];\n"
        + "    for (var i = 0; i < count; i++)\n"
        + "    {\n"
        + "        var carrier = (global::Noesis.ContentControl)carriers.Children[i];\n"
        + "        parts[i] = carrier.Tag;\n"
        + "        carrier.ClearValue(global::Noesis.FrameworkElement.TagProperty);\n"
        + "    }\n"
        + "    return parts;\n"
        + "}";

    static bool IsTemplateBinding(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration
        && attribute.Name.NamespaceName != XamlTypeResolver.DirectiveNs
        && XamlMarkupParser.IsMarkup(attribute.Value)
        && XamlMarkupParser.Parse(attribute.Value) is { Name: "TemplateBinding" };

    static IEnumerable<XAttribute> TemplateBindingsOf(XElement element) =>
        element.Attributes().Where(IsTemplateBinding);

    // A nested template is planned when it is built; a property element is not an object.
    IEnumerable<XElement> OwnContent(XElement scope)
    {
        var pending = new Stack<XElement>(scope.Elements().Reverse());
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            var symbol = resolver.SymbolOf(element);
            if (!element.Name.LocalName.Contains("."))
                yield return element;

            if (
                symbol is not null
                && XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate")
            )
                continue;

            foreach (var child in element.Elements().Reverse())
                pending.Push(child);
        }
    }

    // Mirrors the constructing path: any element built another way would leave its instance unused.
    bool Graftable(XElement element) =>
        element.Name.LocalName is not ("StaticResource" or "DynamicResource")
        && !(
            element.Name.LocalName == "ResourceDictionary"
            && element.Attribute("Source") is not null
        )
        && resolver.SymbolOf(element) is { } symbol
        && !IsSetterLike(symbol)
        && !HasUnassignableSlot(element, symbol)
        && !NeedsParsedSetter(element, symbol)
        && !(
            !element.HasElements
            && (symbol.SpecialType != SpecialType.None || symbol.TypeKind == TypeKind.Struct)
            && LiteralOf(element, symbol) is not null
        )
        && !symbol.IsAbstract
        && resolver.HasConstructor(symbol);

    // A ControlTemplate set through a Style's Template setter resolves its bindings against the Style's type.
    static XAttribute? TargetTypeOf(XElement template) =>
        (template.Attribute("TargetType") is null ? SetterStyle(template) : template)?.Attribute(
            "TargetType"
        )
            is { } target
            ? new XAttribute(target)
            : null;

    /// <summary>Some Noesis types exist only natively and have no managed class to construct,
    /// so that subtree stays on the XAML parser.</summary>
    string? EmitParserFallback(XElement element, ITypeSymbol? expected)
    {
        var clone = new XElement(element);

        clone.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs))?.Remove();

        CopyNamespaces(clone, element);

        var parsedType = resolver.SymbolOf(element);
        var deferred = new List<XAttribute>();
        foreach (var attribute in clone.Attributes().ToArray())
        {
            if (attribute.IsNamespaceDeclaration || !NamesAResource(attribute.Value))
                continue;

            if (parsedType is null)
            {
                return Fail(
                    $"<{element.Name.LocalName}> stays on the parser and its {attribute.Name.LocalName} "
                        + "names a resource, but the type is unresolved so it cannot be assigned after"
                );
            }

            deferred.Add(attribute);
            attribute.Remove();
        }

        foreach (var descendant in clone.Descendants())
        {
            foreach (var attribute in descendant.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || !NamesAResource(attribute.Value))
                    continue;

                foreach (var key in ResourceKeysIn(descendant))
                {
                    if (DeclaredIn(clone, key) is null)
                        Errors.Add(
                            $"<{descendant.Name.LocalName} {attribute.Name.LocalName}> names "
                                + $"'{key}' inside <{element.Name.LocalName}>, which the parser "
                                + "resolves alone and that subtree does not declare"
                        );
                }
            }
        }

        var cast = CastTypeOr(expected, "global::Noesis.BaseComponent");

        var name = NextName(element.Name.LocalName);
        _lines.Add(
            $"var {name} = ({cast})global::Noesis.GUI.ParseXaml({Verbatim(clone.ToString())});"
        );

        RegisterParsedNames(element, name);

        foreach (var attribute in deferred)
            ApplyAttribute(element, name, parsedType!, attribute);

        return name;
    }

    // A parsed fragment registers its names into its own scope, which the document never reaches.
    void RegisterParsedNames(XElement fragment, string parsed)
    {
        if (_templates.Count == 0 && _rootClass is null)
            return;

        var named = fragment
            .DescendantsAndSelf()
            .Where(e =>
                !e.Ancestors()
                    .TakeWhile(a => a != fragment.Parent)
                    .Any(a =>
                        resolver.SymbolOf(a) is { } owner
                        && XamlTypeResolver.DerivesFrom(owner, "global::Noesis.FrameworkTemplate")
                    )
            )
            .Select(e =>
                (
                    Element: e,
                    Name: e.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs))?.Value
                )
            )
            .Where(n => n.Name is not null)
            .ToList();

        if (named.Count == 0)
            return;

        var scope = NextName("parsedscope");
        _lines.Add(
            $"var {scope} = global::Noesis.NameScope.GetNameScope((global::Noesis.DependencyObject)(object){parsed});"
        );

        foreach (var (element, name) in named)
        {
            var found = element == fragment ? parsed : $"{scope}.FindName({Quote(name!)})";

            if (_templates.Count > 0)
            {
                _lines.Add(
                    $"{_templates[_templates.Count - 1]}.RegisterName({Quote(name!)}, {found});"
                );
                continue;
            }

            if (resolver.SymbolOf(element) is { } symbol)
            {
                _lines.Add($"this._{name} = ({XamlTypeResolver.Fqn(symbol)}){found};");
                found = $"this._{name}";
            }

            _lines.Add($"{RootScope}.RegisterName({Quote(name!)}, {found});");
        }
    }

    static XElement StyleProbe(string? targetType, object content) =>
        new XElement(
            XName.Get("Style", XamlTypeResolver.PresentationNs),
            new XAttribute(XName.Get("Key", XamlTypeResolver.DirectiveNs), "__p"),
            targetType is null ? null : new XAttribute("TargetType", targetType),
            content
        );

    // DynamicResource is exempt: it resolves against the live tree, long after this parse.
    static bool NamesAResource(string value) => StaticResourceKey.IsMatch(value);

    static IEnumerable<XAttribute> Namespaces(XElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            foreach (var attribute in e.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    yield return attribute;
            }
        }
    }

    // A fragment the parser sees alone carries no enclosing scope, so every xmlns it names travels.
    static void CopyNamespaces(XElement into, XElement from)
    {
        foreach (var declaration in Namespaces(from))
        {
            if (into.Attribute(declaration.Name) is null)
                into.Add(new XAttribute(declaration.Name, declaration.Value));
        }
    }

    /// <summary>A fragment the parser resolves alone: wrapped in a dictionary that carries its
    /// namespaces, keyed so the caller can read the one entry back out.</summary>
    static XElement Probe(XElement scope, params object?[] content)
    {
        var probe = new XElement(
            XName.Get("ResourceDictionary", XamlTypeResolver.PresentationNs),
            content
        );
        CopyNamespaces(probe, scope);
        return probe;
    }

    static string ParsedStyle(XElement probe) =>
        $"((global::Noesis.Style)((global::Noesis.ResourceDictionary)"
        + $"global::Noesis.GUI.ParseXaml({Verbatim(probe.ToString())}))[\"__p\"])";

    static string WrapAsDictionary(XElement entry) => Probe(entry, new XElement(entry)).ToString();

    static string Verbatim(string value) => "@\"" + value.Replace("\"", "\"\"") + "\"";

    /// <summary>Re-parses just this Style's BasedOn reference, so a theme style still resolves.</summary>
    string BasedOnProbe(XElement style)
    {
        var probe = Probe(
            style,
            StyleProbe(
                style.Attribute("TargetType")?.Value,
                new XAttribute("BasedOn", style.Attribute("BasedOn")!.Value)
            )
        );

        return ParsedStyle(probe) + ".BasedOn";
    }
}
