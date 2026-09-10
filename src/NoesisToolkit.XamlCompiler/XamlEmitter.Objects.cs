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
            _lines.Add(
                $"var {merged} = __resources({Quote(LogicalFor(source.Value))}, {Quote(source.Value)});"
            );
            _usesResourceLookup = true;
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
            element.Nodes().OfType<XText>().FirstOrDefault() is { } literal
            && !element.HasElements
            && (symbol.SpecialType != SpecialType.None || symbol.TypeKind == TypeKind.Struct)
        )
        {
            var text = literal.Value.Trim();
            var converted =
                symbol.SpecialType == SpecialType.System_Double
                    ? NumberLiteral(text, "F")
                    : ConvertValue(element, text, symbol);

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
        _lines.Add(
            $"var {name} = new {XamlTypeResolver.Fqn(symbol)}({StringArgument(element, symbol)});"
        );

        var pushed = PushStyleTarget(element, symbol);
        var isTemplate = XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate");
        if (pushed)
            PushNameScope(element);
        if (isTemplate)
        {
            _templates.Add(name);
            PlanTemplateTriggers(element);
        }

        // Value converts against the type Property names, so Property applies first either way.
        foreach (var attribute in element.Attributes().OrderBy(a => a.Name.LocalName == "Value"))
            ApplyAttribute(element, name, symbol, attribute);

        if (XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.ResourceDictionary"))
            EmitDictionaryBody(element, name);
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

        if (filePath.Length > 0 && resolver.HasConstructor(symbol, "System.Uri", "System.String"))
            return TextConstructor.BaseUriAndSource;

        return resolver.HasConstructor(symbol, "System.String")
            ? TextConstructor.Source
            : TextConstructor.None;
    }

    static string? TextOf(XElement element)
    {
        var text = element.Nodes().OfType<XText>().FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
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
