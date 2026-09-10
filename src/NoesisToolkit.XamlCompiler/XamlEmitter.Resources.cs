using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>Resource keys and the two moments a lookup can happen. A dictionary is built before
/// its graph is assembled, so a key that may live in a sibling waits for <c>Flush</c>; a root is
/// built after the graph is installed and reads it inline.</summary>
sealed partial class XamlEmitter
{
    /// <summary>Resolves a Source= uri to the pack name the registries are keyed by.</summary>
    string LogicalFor(string source) =>
        XamlPaths.SourceLogicalName(source, filePath, packPrefix, projectDir);

    bool _usesResolve;

    bool _usesRootResolve;

    string ResolveExpression(string key)
    {
        _usesResolve = true;
        return $"__resolveIn(null, {ScopeArray()}, {key})";
    }

    // Only legal from _deferredGlobal, whose statements run with the merged root in hand.
    string RootResolveExpression(string key)
    {
        _usesRootResolve = true;
        return $"__resolveIn(__root, {ScopeArray()}, {key})";
    }

    // A dictionary waits for Flush; a root is built after install and reads the graph inline.
    void AddGraphStatement(Func<string, string> statement, string key)
    {
        if (_rootClass is null)
            _deferredGlobal.Add(statement(RootResolveExpression(key)));
        else
            _lines.Add(statement(ResolveExpression(key)));
    }

    string ScopeArray()
    {
        var scopes = new List<string>(_dictionaries);
        scopes.Reverse();
        return $"new global::Noesis.ResourceDictionary[] {{ {string.Join(", ", scopes.ToArray())} }}";
    }

    // An unknown pack name still loads through the XamlProvider, so a partial tree keeps working.
    const string ResourcesHelper =
        "static global::Noesis.ResourceDictionary __resources(string logical, string source)\n"
        + "{\n"
        + "    var d = __XamlResources.Resolve(logical);\n"
        + "    if (d != null) return d;\n"
        + "    return new global::Noesis.ResourceDictionary { Source = new global::System.Uri(source, global::System.UriKind.RelativeOrAbsolute) };\n"
        + "}";

    void EmitLookupHelpers()
    {
        if (!_usesResolve && !_usesRootResolve)
            return;

        _lines.Add(LookupHelper);
        _lines.Add(ResolveInHelper);
    }

    static readonly string LookupHelper = XamlLookupHelper.Emit("__lookup");

    /// <summary>Where a StaticResource is read from, now rather than through the live tree.</summary>
    string ResourceValueExpression(string key) =>
        _dictionary is null
            ? $"global::Noesis.GUI.GetApplicationResources()[{key}]"
            : ResolveExpression(key);

    const string ResolveInHelper =
        "static object __resolveIn(global::Noesis.ResourceDictionary root, global::Noesis.ResourceDictionary[] scopes, object k)\n"
        + "{\n"
        + "    foreach (var s in scopes) { var v = __lookup(s, k); if (v != null) return v; }\n"
        + "    return __lookup(root, k) ?? __lookup(global::Noesis.GUI.GetApplicationResources(), k) ?? __XamlResources.SharedByKey(k);\n"
        + "}";

    void EmitDictionaryBody(XElement root, string target)
    {
        foreach (var child in root.Elements())
        {
            var local = child.Name.LocalName;

            if (local.Contains("."))
            {
                var dot = local.IndexOf('.');
                if (local.Substring(dot + 1) == "MergedDictionaries")
                {
                    foreach (var merged in child.Elements())
                    {
                        var item = EmitObject(merged);
                        if (item is not null)
                            _lines.Add($"{target}.MergedDictionaries.Add({item});");
                    }

                    continue;
                }

                Errors.Add($"<{local}> on a ResourceDictionary is not supported");
                continue;
            }

            if (ResourceKey(child) is not { } resourceKey)
                continue;

            if (resourceKey.Key is not { } key)
            {
                EmitUnmanagedKeyEntry(child, target);
                continue;
            }

            var value = EmitObject(child);
            if (value is null)
                continue;

            _lines.Add($"{target}.Add({key}, {value});");
        }
    }

    // Add takes only string and Type keys; anything else rides in as a merged dictionary.
    void EmitUnmanagedKeyEntry(XElement entry, string target)
    {
        var escaped = new XElement(entry);
        foreach (var declaration in Namespaces(entry))
        {
            if (escaped.Attribute(declaration.Name) is null)
                escaped.Add(new XAttribute(declaration.Name, declaration.Value));
        }

        var basedOn = escaped.Attribute("BasedOn");
        var basedOnKey =
            basedOn is not null
            && XamlMarkupParser.IsMarkup(basedOn.Value)
            && XamlMarkupParser.Parse(basedOn.Value) is { Name: "StaticResource" } reference
                ? ResourceKeyExpression(entry, reference)
                : null;

        if (basedOnKey is not null)
            basedOn!.Remove();

        var parsed = NextName("escaped");
        _lines.Add(
            $"var {parsed} = (global::Noesis.ResourceDictionary)global::Noesis.GUI.ParseXaml({Verbatim(WrapAsDictionary(escaped))});"
        );

        if (basedOnKey is not null)
            AddGraphStatement(
                found =>
                    $"foreach (var __k in {parsed}.Keys) if ({parsed}[__k] is global::Noesis.Style __s) __s.BasedOn = (global::Noesis.Style){found};",
                basedOnKey
            );

        _lines.Add($"{target}.MergedDictionaries.Add({parsed});");
    }

    /// <summary>The keys this file declares, as the expressions a lookup compares against.</summary>
    public List<(string Key, string? Leaf)> DeclaredKeys(XElement root)
    {
        var keys = new List<(string Key, string? Leaf)>();
        Collect(root);
        return keys;

        void Collect(XElement dictionary)
        {
            foreach (var child in dictionary.Elements())
            {
                var local = child.Name.LocalName;
                var dot = local.IndexOf('.');
                if (dot >= 0)
                {
                    if (local.Substring(dot + 1) != "MergedDictionaries")
                        continue;

                    foreach (var merged in child.Elements())
                    {
                        if (
                            merged.Name.LocalName == "ResourceDictionary"
                            && merged.Attribute("Source") is null
                        )
                            Collect(merged);
                    }

                    continue;
                }

                if (ResourceKey(child) is { Key: { } key })
                    keys.Add((key, LeafType(child)));
            }
        }
    }

    // Handed out without building the declaring dictionary, so it is reachable from inside that build.
    string? LeafType(XElement element)
    {
        if (element.HasElements || element.Nodes().OfType<XText>().Any())
            return null;

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;
            if (attribute.Name != XName.Get("Key", XamlTypeResolver.DirectiveNs))
                return null;
        }

        var symbol = resolver.SymbolOf(element);
        if (symbol is null || symbol.IsAbstract || symbol.IsStatic)
            return null;

        return resolver.HasConstructor(symbol) ? XamlTypeResolver.Fqn(symbol) : null;
    }

    /// <summary>The key expression an entry is added under. Null where the entry is not keyed at
    /// all; a null <c>Key</c> where only the parser can produce it.</summary>
    readonly struct ResourceKeyResult(string? key)
    {
        public string? Key { get; } = key;
    }

    ResourceKeyResult? Failed(string message)
    {
        Errors.Add(message);
        return null;
    }

    ResourceKeyResult? ResourceKey(XElement element)
    {
        var explicitKey = element.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs));
        if (explicitKey is not null)
        {
            var raw = explicitKey.Value;
            if (!XamlMarkupParser.IsMarkup(raw))
                return new ResourceKeyResult(Quote(raw));

            var typeKey = ResolveTypeSymbol(element, raw);
            if (typeKey is null)
            {
                return new ResourceKeyResult(null);
            }

            return new ResourceKeyResult($"typeof({XamlTypeResolver.Fqn(typeKey)})");
        }

        var dataType = element.Attribute("DataType");
        if (dataType is not null)
        {
            var type = ResolveTypeReference(element, dataType.Value);
            if (type is null)
            {
                return Failed($"could not resolve DataType '{dataType.Value}'");
            }

            return new ResourceKeyResult($"typeof({type})");
        }

        var targetType = element.Attribute("TargetType");
        if (targetType is not null)
        {
            var symbol = ResolveTypeSymbol(element, targetType.Value);
            if (symbol is null)
            {
                return Failed($"could not resolve TargetType '{targetType.Value}'");
            }

            return new ResourceKeyResult(KeyExpressionFor(symbol));
        }

        return Failed($"<{element.Name.LocalName}> has no x:Key, DataType or TargetType");
    }

    /// <summary>Must match how the implicit style was keyed on the way in: a native type keys by
    /// bare name, a managed one by Type.</summary>
    static string KeyExpressionFor(INamedTypeSymbol symbol) =>
        symbol.ContainingAssembly?.Name == "Noesis.GUI"
            ? Quote(symbol.Name)
            : $"typeof({XamlTypeResolver.Fqn(symbol)})";

    string? ResourceKeyExpression(XElement element, MarkupCall call)
    {
        if (call.Positional.Count > 0)
            return Quote(call.Positional[0]);

        var nested = call.PositionalCalls.FirstOrDefault();
        if (nested is { Name: "x:Type" or "Type" })
        {
            var symbol = ResolveTypeSymbol(element, nested.Positional.FirstOrDefault() ?? "");
            if (symbol is null)
            {
                return Fail("could not resolve the type in a resource key");
            }

            return KeyExpressionFor(symbol);
        }

        return Fail($"{{{call.Name}}} has no resolvable key");
    }
}
