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

        // A trigger inside a template has no TargetType; the named element supplies one.
        var scopeNamespace = (string?)null;
        var attached = setter.Attribute("Property")?.Value.Contains('.') == true;

        if (declared is null && !attached)
        {
            var named = setter.Attribute("TargetName")?.Value;
            var symbol = named is null ? null : LookupName(named);
            if (symbol is null)
            {
                return Fail("a Setter with a live value needs a TargetType or TargetName in scope");
            }

            declared = "{x:Type __probe:" + symbol.Name + "}";
            scopeNamespace =
                "clr-namespace:"
                + symbol.ContainingNamespace.ToDisplayString()
                + (symbol.ContainingAssembly is { } assembly ? ";assembly=" + assembly.Name : "");
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

        foreach (var attribute in deferred)
            ApplyAttribute(element, name, parsedType!, attribute);

        return name;
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
