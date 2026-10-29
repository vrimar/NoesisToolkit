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

    string SourcedDictionary(string source)
    {
        var logical = LogicalFor(source);

        // The fallback loads with no base uri, so a relative source goes rooted the way the parser names it.
        var fallback = source.Contains(";") || !logical.Contains(";") ? source : "/" + logical;

        _usesResourceLookup = true;
        return $"__resources({Quote(logical)}, {Quote(fallback)})";
    }

    bool _usesResolve;

    bool _usesGraphResolve;

    HashSet<string>? _ownKeys;

    string ResolveExpression(string key)
    {
        _usesResolve = true;
        return $"__resolveIn(null, {ScopeArray()}, {key}, {Shared(key)})";
    }

    // An own key not yet in scope is a forward reference, which the parser leaves unresolved.
    string Shared(string key) => _ownKeys?.Contains(key) == true ? "false" : "true";

    // Resolved where the reference sits, as the parser does; only a miss waits for the flush.
    void AddGraphStatement(
        Func<string, string> statement,
        string key,
        bool skipMissing = false,
        Func<string, string>? settle = null
    )
    {
        settle ??= statement;
        if (_rootClass is not null)
        {
            if (!skipMissing)
            {
                _lines.Add(statement(ResolveExpression(key)));
                return;
            }

            var resolved = NextName("found");
            _lines.Add($"var {resolved} = {ResolveExpression(key)};");
            _lines.Add($"if ({resolved} != null) {{ {statement(resolved)} }}");
            return;
        }

        _usesGraphResolve = true;
        var found = NextName("found");
        var outer = NextName("outer");

        _lines.Add($"var {found} = __scoped({ScopeArray()}, {key});");
        _lines.Add($"if ({found} != null) {{ {statement(found)} }}");
        _deferredGlobal.Add(
            $"if ({found} == null) {{ var {outer} = __outer(__root, {_dictionaries[0]}, {key}, {Shared(key)}); "
                + (skipMissing ? $"if ({outer} != null) {{ {settle(outer)} }}" : settle(outer))
                + " }"
        );
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
        if (!_usesResolve && !_usesGraphResolve)
            return;

        _lines.Add(LookupHelper);

        if (_usesResolve)
            _lines.Add(ResolveInHelper);

        if (_usesGraphResolve)
        {
            _lines.Add(ScopedHelper);
            _lines.Add(OutsideHelper);
            _lines.Add(OuterHelper);
        }
    }

    static readonly string LookupHelper = XamlLookupHelper.Emit("__lookup");

    const string ResolveInHelper =
        "static object __resolveIn(global::Noesis.ResourceDictionary root, global::Noesis.ResourceDictionary[] scopes, object k, bool shared)\n"
        + "{\n"
        + "    foreach (var s in scopes) { var v = __lookup(s, k); if (v != null) return v; }\n"
        + "    return __lookup(root, k) ?? __lookup(global::Noesis.GUI.GetApplicationResources(), k) ?? (shared ? __XamlResources.SharedByKey(k) : null);\n"
        + "}";

    const string ScopedHelper =
        "static object __scoped(global::Noesis.ResourceDictionary[] scopes, object k)\n"
        + "{\n"
        + "    foreach (var s in scopes) { var v = __lookup(s, k); if (v != null) return v; }\n"
        + "    return null;\n"
        + "}";

    // Contains and the indexer see merged dictionaries, so skipping one walks them in indexer order.
    const string OutsideHelper =
        "static object __outside(global::Noesis.ResourceDictionary d, global::Noesis.ResourceDictionary own, object k)\n"
        + "{\n"
        + "    if (d == null || d == own || !d.Contains(k)) return null;\n"
        + "    if (!own.Contains(k)) return d[k];\n"
        + "    var name = k is global::System.Type t ? t.FullName : k;\n"
        + "    foreach (var e in d.Keys) if (object.Equals(e is global::System.Type u ? u.FullName : e, name)) return d[k];\n"
        + "    for (var i = d.MergedDictionaries.Count - 1; i >= 0; i--) { var v = __outside(d.MergedDictionaries[i], own, k); if (v != null) return v; }\n"
        + "    return null;\n"
        + "}";

    const string OuterHelper =
        "static object __outer(global::Noesis.ResourceDictionary root, global::Noesis.ResourceDictionary own, object k, bool shared)\n"
        + "{\n"
        + "    return __outside(root, own, k) ?? __outside(global::Noesis.GUI.GetApplicationResources(), own, k) ?? (shared ? __XamlResources.SharedByKey(k) : null);\n"
        + "}";

    void EmitDictionaryBody(XElement root, string target)
    {
        if (_rootClass is null && _ownKeys is null && root.Document?.Root is { } document)
            _ownKeys = new HashSet<string>(
                DeclaredKeys(document).Select(k => k.Key),
                StringComparer.Ordinal
            );

        var escaped = new List<string>();

        foreach (var child in root.Elements())
        {
            if (IsIgnorable(child))
                continue;

            var local = child.Name.LocalName;

            if (local.Contains("."))
            {
                var dot = local.IndexOf('.');
                if (local.Substring(dot + 1) == "MergedDictionaries")
                {
                    foreach (var merged in child.Elements().Where(e => !IsIgnorable(e)))
                    {
                        if (
                            IsResourceElement(merged)
                            && resolver.Resolve(
                                XamlTypeResolver.PresentationNs,
                                "ResourceDictionary"
                            )
                                is { } dictionaryType
                            && resolver.FindProperty(dictionaryType, "MergedDictionaries")
                                is { } mergedSlot
                        )
                        {
                            AddResourceItem(
                                merged,
                                dictionaryType,
                                mergedSlot.Type,
                                $"{target}.MergedDictionaries"
                            );
                            continue;
                        }

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
                if (EmitUnmanagedKeyEntry(child) is { } parsedEntry)
                    escaped.Add(parsedEntry);
                continue;
            }

            if (local == "StaticResource")
            {
                if (ResourceElementKey(child) is { } reference)
                    AddGraphStatement(
                        found => $"{target}.Add({key}, {found});",
                        reference,
                        skipMissing: true
                    );
                continue;
            }

            var deferred = _deferredGlobal.Count;
            var element =
                resolver.SymbolOf(child) is { } entryType
                && XamlTypeResolver.DerivesFrom(entryType, "global::Noesis.FrameworkElement");
            if (element)
                _keyedElements++;

            string? value;
            try
            {
                value = EmitObject(child);
            }
            finally
            {
                if (element)
                    _keyedElements--;
            }

            if (value is null)
                continue;

            _lines.Add($"{target}.Add({key}, {value});");

            // Add boxed a copy, so a member settled later has to be stored again.
            if (
                _deferredGlobal.Count > deferred
                && resolver.SymbolOf(child) is { TypeKind: TypeKind.Struct }
            )
                _deferredGlobal.Add($"{target}[{key}] = {value};");
        }

        // An entry of the dictionary's own outranks every merged one, which these ride in as.
        foreach (var parsed in escaped)
            _lines.Add($"{target}.MergedDictionaries.Add({parsed});");
    }

    // Add takes only string and Type keys; anything else rides in as a merged dictionary.
    string? EmitUnmanagedKeyEntry(XElement entry)
    {
        var escaped = new XElement(entry);
        CopyScope(escaped, entry);

        var basedOn = escaped.Attribute("BasedOn");
        var basedOnKey =
            basedOn is not null
            && XamlMarkupParser.IsMarkup(basedOn.Value)
            && XamlMarkupParser.Parse(basedOn.Value) is { Name: "StaticResource" } reference
                ? ResourceKeyExpression(entry, reference)
                : null;

        if (basedOnKey is not null)
            basedOn!.Remove();

        const string reason = "resource-left-to-the-parser";
        RootParsed(entry, reason);

        var probe = Probe(entry, new XElement(escaped));
        if (
            Carry(
                entry,
                probe,
                ResourceKeysIn(escaped),
                reason,
                $"<{entry.Name.LocalName}> left to the parser"
            )
            is not { } carried
        )
            return null;

        var parsed = NextName("escaped");
        _lines.Add(
            $"var {parsed} = (global::Noesis.ResourceDictionary)global::Noesis.GUI.ParseXaml({Quote(Serialize(probe))});"
        );

        // The copies only fed the parse; the dictionary's own entries stay the ones it reads.
        foreach (var key in carried)
            _lines.Add($"{parsed}.Remove({Quote(key)});");

        if (basedOnKey is not null)
        {
            var theme = BasedOnProbe(entry);
            AddGraphStatement(
                found =>
                    $"foreach (var __k in {parsed}.Keys) if ({parsed}[__k] is global::Noesis.Style __s) __s.BasedOn = (global::Noesis.Style)({found} ?? {theme});",
                basedOnKey
            );
        }

        return parsed;
    }

    /// <summary>The keys this file declares, as the expressions a lookup compares against.</summary>
    public List<(string Key, string? Leaf)> DeclaredKeys(XElement root)
    {
        var keys = new List<(string Key, string? Leaf)>();

        // A query only: building each entry is what reports a bad key.
        using (Speculate())
            Collect(root);
        return keys;

        void Collect(XElement dictionary)
        {
            foreach (var child in dictionary.Elements().Where(e => !IsIgnorable(e)))
            {
                var local = child.Name.LocalName;
                var dot = local.IndexOf('.');
                if (dot >= 0)
                {
                    if (local.Substring(dot + 1) != "MergedDictionaries")
                        continue;

                    foreach (var merged in child.Elements().Where(e => !IsIgnorable(e)))
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
        if (element.HasElements || TextOf(element) is not null)
            return null;

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;
            if (attribute.Name != XName.Get("Key", XamlTypeResolver.DirectiveNs))
                return null;
        }

        var symbol = resolver.SymbolOf(element);
        if (
            symbol is null
            || symbol.IsAbstract
            || symbol.IsStatic
            || !resolver.IsAccessible(symbol, null)
        )
            return null;

        return resolver.HasConstructor(symbol) ? XamlTypeResolver.Fqn(symbol) : null;
    }

    /// <summary>The key expression an entry is added under. Null where the entry is not keyed at
    /// all; a null <c>Key</c> where only the parser can produce it.</summary>
    readonly struct ResourceKeyResult(string? key)
    {
        public string? Key { get; } = key;
    }

    ResourceKeyResult? Failed(string message, System.Xml.IXmlLineInfo at)
    {
        Errors.Add(message, at);
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
            if (
                typeKey is null
                || UnknownToParser(typeKey)
                || !resolver.IsAccessible(typeKey, _rootClass)
            )
            {
                return new ResourceKeyResult(null);
            }

            return new ResourceKeyResult(KeyExpressionFor(typeKey));
        }

        var dataType = element.Attribute("DataType");
        if (dataType is not null)
        {
            var type = ResolveTypeSymbol(element, dataType.Value);
            if (type is null)
            {
                return Failed($"could not resolve DataType '{dataType.Value}'", dataType);
            }

            return new ResourceKeyResult(
                UnknownToParser(type) || !resolver.IsAccessible(type, _rootClass)
                    ? null
                    : KeyExpressionFor(type)
            );
        }

        var targetType = element.Attribute("TargetType");
        if (
            targetType is not null
            && resolver.SymbolOf(element) is { } keyed
            && !XamlTypeResolver.DerivesFrom(keyed, "global::Noesis.Style")
        )
        {
            // Only a Style is keyed by its TargetType; the parser drops anything else left unkeyed.
            DeadMarkup.Add($"<{element.Name.LocalName}> has no x:Key and is dropped");
            return null;
        }

        if (targetType is not null)
        {
            var symbol = ResolveTypeSymbol(element, targetType.Value);
            if (symbol is null)
            {
                return Failed($"could not resolve TargetType '{targetType.Value}'", targetType);
            }

            return new ResourceKeyResult(
                resolver.IsAccessible(symbol, _rootClass) ? KeyExpressionFor(symbol) : null
            );
        }

        return Failed($"<{element.Name.LocalName}> has no x:Key, DataType or TargetType", element);
    }

    /// <summary>Must match how the implicit style was keyed on the way in: a native type, or a System
    /// type the parser maps to one, keys by its native name; a managed one by Type.</summary>
    static string KeyExpressionFor(INamedTypeSymbol symbol) =>
        symbol.ContainingAssembly?.Name == "Noesis.GUI" ? Quote(symbol.Name)
        : NativeSystemName(symbol) is { } native ? Quote(native)
        : $"typeof({XamlTypeResolver.Fqn(symbol)})";

    // The parser's own names for the System types it maps natively; the rest it keys by full name.
    static string? NativeSystemName(INamedTypeSymbol symbol) =>
        symbol.SpecialType switch
        {
            SpecialType.System_String => "String",
            SpecialType.System_Boolean => "Bool",
            SpecialType.System_Int16 => "Int16",
            SpecialType.System_UInt16 => "UInt16",
            SpecialType.System_Int32 => "Int32",
            SpecialType.System_UInt32 => "UInt32",
            SpecialType.System_Single => "Single",
            SpecialType.System_Double => "Double",
            _ => XamlTypeResolver.Fqn(symbol) == "global::System.TimeSpan" ? "TimeSpan" : null,
        };

    // The parser drops an entry keyed by one of these as an unknown type.
    static bool UnknownToParser(INamedTypeSymbol symbol) =>
        symbol.SpecialType
            is SpecialType.System_Object
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
        || XamlTypeResolver.Fqn(symbol) is "global::System.Uri" or "global::System.Type";

    MarkupCall? ResourceElementCall(XElement element)
    {
        var reference = new MarkupCall { Name = element.Name.LocalName };
        var raw = element.Attribute("ResourceKey")?.Value;
        if (raw is null)
        {
            Errors.Add($"<{element.Name.LocalName}> has no ResourceKey");
            return null;
        }

        if (!XamlMarkupParser.IsMarkup(raw))
        {
            reference.Positional.Add(raw.Trim());
            return reference;
        }

        if (!TryMarkup(raw, out var nested))
            return null;

        reference.PositionalCalls.Add(nested);
        return reference;
    }

    string? ResourceElementKey(XElement element) =>
        ResourceElementCall(element) is { } call ? ResourceKeyExpression(element, call) : null;

    string? ResourceKeyExpression(XElement element, MarkupCall call)
    {
        if (call.Positional.Count > 0)
            return Quote(call.Positional[0]);

        var named = call.Named.FirstOrDefault(p => p.Key == "ResourceKey").Value;
        if (named is string text)
            return Quote(text);

        var nested = call.PositionalCalls.FirstOrDefault() ?? named as MarkupCall;
        if (nested is { Name: "x:Type" or "Type" })
        {
            var symbol = ResolveTypeSymbol(element, TypeArgument(nested));
            if (symbol is null)
            {
                return Fail("could not resolve the type in a resource key");
            }

            return KeyExpressionFor(symbol);
        }

        return Fail($"{{{call.Name}}} has no resolvable key");
    }
}
