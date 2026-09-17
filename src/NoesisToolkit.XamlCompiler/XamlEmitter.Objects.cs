using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace NoesisToolkit.Xaml;

/// <summary>Constructing an element: the object itself, and the text content some types take
/// through a constructor rather than a content property.</summary>
sealed partial class XamlEmitter
{
    public string? EmitObject(XElement element) => EmitObject(element, null);

    public string? EmitObject(XElement element, ITypeSymbol? expected)
    {
        if (_suppressed.Remove(element))
            return null;

        if (
            element.Name.LocalName == "ResourceDictionary"
            && element.Attribute("Source") is { } source
        )
        {
            var merged = NextName("merged");
            var logical = LogicalFor(source.Value);

            // The fallback loads with no base uri, so a relative source goes rooted the way the parser names it.
            var fallback =
                source.Value.Contains(";") || !logical.Contains(";") ? source.Value : "/" + logical;

            _lines.Add($"var {merged} = __resources({Quote(logical)}, {Quote(fallback)});");
            _usesResourceLookup = true;

            if (element.HasElements)
                EmitScopedDictionaryBody(element, merged);

            return merged;
        }

        if (element.Name.LocalName is "StaticResource" or "DynamicResource")
        {
            var resourceKey = element.Attribute("ResourceKey")?.Value;
            if (resourceKey is null)
            {
                return Fail($"<{element.Name.LocalName}> has no ResourceKey");
            }

            var resolved = NextName("resource");
            var cast = CastPrefix(expected);

            _lines.Add($"var {resolved} = {cast}{ResolveExpression(Quote(resourceKey))};");
            return resolved;
        }

        var symbol = resolver.SymbolOf(element);
        if (symbol is null)
            return EmitParserFallback(element, expected);

        if (HasUnassignableSlot(element, symbol))
            return EmitParserFallback(element, expected);

        if (NeedsParsedSetter(element, symbol))
            return EmitSetterProbe(element);

        if (
            !element.HasElements
            && (symbol.SpecialType != SpecialType.None || symbol.TypeKind == TypeKind.Struct)
            && LiteralOf(element, symbol) is { } literal
        )
        {
            var converted =
                symbol.SpecialType == SpecialType.System_Double
                    ? NumberLiteral(literal, "F")
                    : ConvertValue(element, literal, symbol);

            if (converted is null)
                return null;

            var boxed = NextName(symbol.Name);
            _lines.Add($"var {boxed} = {converted};");
            return boxed;
        }

        var constructible = !symbol.IsAbstract && resolver.HasConstructor(symbol);

        if (!constructible && XamlTypeResolver.HasStringParse(symbol))
        {
            if (TextOf(element) is not { } text)
            {
                return Fail($"<{element.Name.LocalName}> is abstract and has no text to parse");
            }

            var parsed = NextName(element.Name.LocalName);
            _lines.Add($"var {parsed} = {XamlTypeResolver.Fqn(symbol)}.Parse({Quote(text)});");
            return parsed;
        }

        if (!constructible)
            return EmitParserFallback(element, expected);

        var name = NextName(element.Name.LocalName);
        _elementVars[element] = name;
        if (_grafts.TryGetValue(element, out var grafted))
        {
            _grafted.Add(element);
            _lines.Add($"var {name} = ({XamlTypeResolver.Fqn(symbol)}){grafted};");
        }
        else
        {
            _lines.Add(
                $"var {name} = new {XamlTypeResolver.Fqn(symbol)}({StringArgument(element, symbol)});"
            );
        }

        // A parsed template carries only what its markup sets, not content a constructor loaded.
        if (
            _templates.Count > 0
            && symbol.ContainingAssembly?.Name != "Noesis.GUI"
            && XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.ContentControl")
            && element.Attribute("Content") is null
        )
            _lines.Add($"{name}.ClearValue(global::Noesis.ContentControl.ContentProperty);");

        var pushed = PushStyleTarget(element, symbol);
        var isTemplate = XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate");
        if (pushed)
            PushNameScope(element);
        if (isTemplate)
        {
            _templates.Add(name);
            PlanTemplateTriggers(element);
            PlanTemplateBindings(element, element);
        }

        // Value converts against the type Property names, so Property applies first either way.
        foreach (var attribute in element.Attributes().OrderBy(a => a.Name.LocalName == "Value"))
            ApplyAttribute(element, name, symbol, attribute);

        if (XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.ResourceDictionary"))
            EmitScopedDictionaryBody(element, name);
        else
            ApplyChildren(element, name, symbol);

        if (_pendingWires.TryGetValue(element, out var pending))
        {
            _pendingWires.Remove(element);
            if (!_wires.TryGetValue(name, out var wires))
                _wires[name] = wires = new List<string>();

            wires.AddRange(pending);
        }

        FlushWires(name);

        if (pushed)
        {
            _styleTargets.RemoveAt(_styleTargets.Count - 1);
            _nameScopes.RemoveAt(_nameScopes.Count - 1);
        }
        if (isTemplate)
            _templates.RemoveAt(_templates.Count - 1);

        return name;
    }

    enum TextConstructor
    {
        None,
        Source,
        BaseUriAndSource,
    }

    // A FontFamily names its source as text; dropped, it leaves a silently empty object.
    TextConstructor TextConstructorOf(XElement element, INamedTypeSymbol symbol)
    {
        if (element.HasElements || TextOf(element) is null)
            return TextConstructor.None;

        // The parser turns the text into an inline, which a Style's Text setter adds to, not replaces.
        if (ContentSlot(symbol).Property is { } content && IsInlineCollection(content.Type))
            return TextConstructor.None;

        if (filePath.Length > 0 && resolver.HasConstructor(symbol, "System.Uri", "System.String"))
            return TextConstructor.BaseUriAndSource;

        return resolver.HasConstructor(symbol, "System.String")
            ? TextConstructor.Source
            : TextConstructor.None;
    }

    // Text a property element splits keeps only its last run, as the parser reads it.
    string? TextOf(XElement element) =>
        ContentNodes(element, null).OfType<string>().LastOrDefault();

    // Whitespace alone still makes an empty string, where every other type has nothing to convert.
    string? LiteralOf(XElement element, INamedTypeSymbol symbol) =>
        TextOf(element)
        ?? (
            symbol.SpecialType == SpecialType.System_String && element.Nodes().OfType<XText>().Any()
                ? ""
                : null
        );

    void EmitScopedDictionaryBody(XElement element, string dictionary)
    {
        _dictionaries.Add(dictionary);
        EmitDictionaryBody(element, dictionary);
        _dictionaries.RemoveAt(_dictionaries.Count - 1);
    }

    // A relative source only means anything against the file that wrote it, named the way the
    // parser names a document it reached through the graph: rooted, not as the caller spelled it.
    string BaseUri() =>
        $"new global::System.Uri(\"/{XamlPaths.LogicalName(filePath, packPrefix, projectDir)}\", global::System.UriKind.RelativeOrAbsolute)";

    string StringArgument(XElement element, INamedTypeSymbol symbol) =>
        TextConstructorOf(element, symbol) switch
        {
            TextConstructor.BaseUriAndSource => $"{BaseUri()}, {Quote(TextOf(element)!)}",
            TextConstructor.Source => Quote(TextOf(element)!),
            _ => "",
        };
}
