using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>
/// Compiles every XAML document into C# that builds the same object graph the native Noesis parser
/// would, so the tree is constructed without runtime XAML parsing.
/// </summary>
[Generator]
public sealed class XamlCompileGenerator : IIncrementalGenerator
{
    static readonly DiagnosticDescriptor Survey = new(
        "NTK1002",
        "XAML compiler coverage",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor Unsupported = new(
        "NTK1001",
        "XAML construct not supported by the compiler",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    // Roslyn reports a throwing generator as a bare exception type, naming no file.
    static readonly DiagnosticDescriptor Crashed = new(
        "NTK1003",
        "XAML compiler failed",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    const string NoRootClass = "root has no x:Class and is not a ResourceDictionary";

    internal const string OptionsStep = "NoesisXamlOptions";

    internal const string DocumentsStep = "NoesisXamlDocuments";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context
            .AnalyzerConfigOptionsProvider.Select((o, _) => XamlCompilerOptions.Read(o))
            .WithTrackingName(OptionsStep);

        var files = context
            .AdditionalTextsProvider.Combine(options)
            .Where(pair => pair.Right.Matches(pair.Left.Path))
            .Select((pair, ct) => (pair.Left.Path, Text: pair.Left.GetText(ct)?.ToString()))
            .WithTrackingName(DocumentsStep);

        var compiled = files
            .Combine(context.CompilationProvider)
            .Combine(options)
            .Select(
                (pair, _) =>
                {
                    var (((path, text), compilation), opts) = pair;
                    if (
                        string.IsNullOrWhiteSpace(text)
                        || compilation.GetTypeByMetadataName("Noesis.GUI") is null
                    )
                        return null;

                    // The emitted graph names the runtime by string. Without it the generated file
                    // would not compile, so a project that installed the compiler alone gets the
                    // native parser rather than a build error.
                    var runtime =
                        compilation.GetTypeByMetadataName(
                            "NoesisToolkit.Mvvm.CodeGen.CompiledBinding"
                        )
                        is not null;

                    return Guarded(
                        path,
                        () => CompileDocument(path, text!, compilation, opts, runtime)
                    );
                }
            );

        context.RegisterSourceOutput(
            compiled.Collect().Combine(context.CompilationProvider),
            (spc, pair) =>
            {
                var (all, compilation) = pair;
                var documents = all.OfType<CompiledDocument>().ToList();
                if (documents.Count == 0)
                    return;

                var registry = Guarded("registry", () => EmitRegistry(documents, compilation));
                Publish(spc, registry);
            }
        );

        context.RegisterSourceOutput(
            compiled,
            (spc, document) =>
            {
                if (document is not null)
                    Publish(spc, document);
            }
        );
    }

    static CompiledDocument Guarded(string what, Func<CompiledDocument> compile)
    {
        try
        {
            return compile();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CompiledDocument(what)
            {
                Crash =
                    $"{what}: {ex.GetType().Name}: {ex.Message} | "
                    + string.Join(" | ", (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim())),
            };
        }
    }

    /// <summary>Reports what one document recorded, then adds its source unless an error blocks it.
    /// Both outputs publish through here, so a diagnostic is raised by exactly one of them.</summary>
    static void Publish(SourceProductionContext spc, CompiledDocument document)
    {
        if (document.Crash is { } crash)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Crashed, null, crash));
            return;
        }

        foreach (var dead in document.DeadMarkup)
            spc.ReportDiagnostic(
                Diagnostic.Create(Survey, null, $"DEAD {document.File} :: {dead}")
            );

        foreach (var error in document.Errors)
            spc.ReportDiagnostic(
                Diagnostic.Create(Unsupported, null, $"{document.File} :: {error}")
            );

        if (document.Source is null || (document.Gated && document.Errors.Count > 0))
            return;

        Output.Add(spc, document.HintName!, document.Source);
    }

    static CompiledDocument EmitRegistry(
        IReadOnlyList<CompiledDocument> all,
        Compilation compilation
    )
    {
        var w = new CodeWriter();
        var ok = 0;
        var fail = 0;
        var loaders = 0;
        var rows = new List<string>();
        var tally = new BindingTally();

        var dictionaries = new List<string>();
        var keyOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        var keyLeaves = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguousKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var document in all)
        {
            var file = document.File;

            if (document.Crash is not null)
            {
                fail++;
                rows.Add($"CRASH {file} :: the compiler failed on this document");
                continue;
            }

            // The loader still parses it, so no graph is emitted and nothing here is counted.
            if (document.NeedsLoader)
            {
                loaders++;
                rows.Add($"LOAD {file} :: parsed at run time, no binding compiled");
                continue;
            }

            tally.Add(document.Tally);

            foreach (var dead in document.DeadMarkup.Distinct())
                rows.Add($"DEAD {file} :: {dead}");

            if (document.Errors.Count > 0)
            {
                fail++;
                foreach (var error in document.Errors.Distinct())
                    rows.Add($"FAIL {file} :: {error}");
                continue;
            }

            ok++;
            rows.Add($"OK   {file}");
            foreach (var reason in document.Tally.Breakdown())
                rows.Add($"     {file} :: fallback {reason.Value}  {reason.Key}");
            foreach (var reason in document.Tally.TriggerBreakdown())
                rows.Add($"     {file} :: trigger {reason.Value}  {reason.Key}");

            if (document.DictionaryRow is not { } row)
                continue;

            dictionaries.Add(row);

            // Two files claiming one key make the owner a matter of merge order.
            var logical = document.Logical!;
            foreach (var (key, leaf) in document.DeclaredKeys)
            {
                if (keyOwners.TryGetValue(key, out var owner) && owner != logical)
                    ambiguousKeys[key] = $"{owner} and {logical}";
                else
                    keyOwners[key] = logical;

                if (leaf is not null)
                    keyLeaves[key] = leaf;
            }
        }

        // Dropping the key disables the compile-time fallback, so name the files that collided.
        foreach (var ambiguous in ambiguousKeys)
        {
            keyOwners.Remove(ambiguous.Key);
            keyLeaves.Remove(ambiguous.Key);
            rows.Add(
                $"DEAD key {ambiguous.Key} :: declared by {ambiguous.Value}; resolved through the graph only"
            );
        }

        w.Line("// XAML compiler coverage survey");
        w.Line(
            $"// files ok: {ok}   files with gaps: {fail}   files left to the loader: {loaders}"
        );
        w.Line($"// bindings: {tally}");
        var split = tally.Split();
        w.Line(
            $"// left native: gap {split.Gap}   boundary {split.Boundary}   author {split.Author}"
        );
        w.Line("// only the gap count is work; a boundary must stay native to keep behaviour");
        foreach (var reason in tally.Breakdown())
            w.Line($"// fallback {reason.Value, 5}  {Refusals.Of(reason.Key), -8}  {reason.Key}");
        w.Line($"// {tally.TriggerLine()}");
        foreach (var reason in tally.TriggerBreakdown())
            w.Line($"// trigger  {reason.Value, 5}  {Refusals.Of(reason.Key), -8}  {reason.Key}");
        foreach (var row in rows)
            w.Line("// " + row.Replace("\r", " ").Replace("\n", " "));
        var chain = new List<string>();
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var candidate = GeneratedNamespace(reference.Name) + ".XamlResources";
            var link = "global::" + candidate;

            // Two assembly names differing only in punctuation share one generated namespace.
            if (compilation.GetTypeByMetadataName(candidate) is not null && !chain.Contains(link))
                chain.Add(link);
        }

        using (w.Block($"namespace {GeneratedNamespace(compilation)}"))
        using (w.Block("public static class XamlResources"))
        {
            w.Line(
                "static readonly global::System.Collections.Generic.List<global::System.Action<global::Noesis.ResourceDictionary>> __pending = new();"
            );
            w.Line("static global::Noesis.ResourceDictionary __graph;");

            // Built after the flush: nothing of its own is sealed, and no drain is coming.
            using (
                w.Block(
                    "public static void Defer(global::System.Action<global::Noesis.ResourceDictionary> fixup)"
                )
            )
            {
                w.Line("if (__graph != null) { fixup(__graph); return; }");
                w.Line("__pending.Add(fixup);");
            }

            using (w.Block("public static void Flush(global::Noesis.ResourceDictionary root)"))
            {
                w.Line("__graph = root;");
                // Indexed: settling one entry can build another dictionary, which appends.
                w.Line("for (var i = 0; i < __pending.Count; i++) __pending[i](root);");
                w.Line("__pending.Clear();");
                foreach (var link in chain)
                    w.Line($"{link}.Flush(root);");
            }

            using (
                w.Block("public static global::Noesis.ResourceDictionary Resolve(string logical)")
            )
            {
                w.Line("var own = __build(logical);");
                w.Line("if (own != null) return own;");

                for (var i = 0; i < chain.Count; i++)
                {
                    w.Line($"var from{i} = {chain[i]}.Resolve(logical);");
                    w.Line($"if (from{i} != null) return from{i};");
                }

                w.Line("return null;");
            }

            w.Line(
                "static readonly global::System.Collections.Generic.Dictionary<string, global::Noesis.ResourceDictionary> __shared = new();"
            );
            w.Line(
                "static readonly global::System.Collections.Generic.HashSet<string> __building = new();"
            );
            w.Line(
                "static readonly global::System.Collections.Generic.Dictionary<object, string> __owners = new()"
            );
            using (w.Block("", "};"))
            {
                foreach (var owner in keyOwners)
                    w.Line($"[{owner.Key}] = @\"{owner.Value}\",");
            }

            w.Line(
                "static readonly global::System.Collections.Generic.Dictionary<object, global::System.Func<object>> __leafFactories = new()"
            );
            using (w.Block("", "};"))
            {
                foreach (var leaf in keyLeaves)
                    w.Line($"[{leaf.Key}] = static () => new {leaf.Value}(),");
            }

            using (w.Block("static global::Noesis.ResourceDictionary __build(string logical)"))
            {
                using (w.Block("foreach (var entry in XamlRegistry.Dictionaries)"))
                using (w.Block("if (entry.Logical == logical)"))
                {
                    w.Line("return entry.Build();");
                }

                w.Line("return null;");
            }

            // A value source only: sharing one instance across merge points changes style resolution.
            using (
                w.Block(
                    "static global::Noesis.ResourceDictionary __sharedDictionary(string logical)"
                )
            )
            {
                w.Line("if (__shared.TryGetValue(logical, out var hit)) return hit;");
                w.Line("if (!__building.Add(logical)) return null;");
                using (w.Block("try"))
                {
                    w.Line("var built = __build(logical);");
                    w.Line("if (built != null) __shared[logical] = built;");
                    w.Line("return built;");
                }
                using (w.Block("finally"))
                {
                    w.Line("__building.Remove(logical);");
                }
            }

            w.Line(XamlLookupHelper.Emit("__lookupIn"));

            w.Line(
                "static readonly global::System.Collections.Generic.Dictionary<object, object> __leaves = new();"
            );

            using (w.Block("public static object SharedByKey(object key)"))
            {
                using (w.Block("if (__leafFactories.TryGetValue(key, out var make))"))
                {
                    w.Line("if (__leaves.TryGetValue(key, out var hit)) return hit;");
                    w.Line("var made = make();");
                    w.Line("__leaves[key] = made;");
                    w.Line("return made;");
                }

                using (w.Block("if (__owners.TryGetValue(key, out var logical))"))
                {
                    w.Line("return __lookupIn(__sharedDictionary(logical), key);");
                }

                for (var i = 0; i < chain.Count; i++)
                {
                    w.Line($"var shared{i} = {chain[i]}.SharedByKey(key);");
                    w.Line($"if (shared{i} != null) return shared{i};");
                }

                w.Line("return null;");
            }
        }

        using (w.Block($"namespace {GeneratedNamespace(compilation)}"))
        using (w.Block("public static class XamlRegistry"))
        {
            w.Line(
                "public static readonly (string Path, string Logical, global::System.Func<global::Noesis.ResourceDictionary> Build)[] Dictionaries ="
            );
            using (w.Block("", "};"))
            {
                foreach (var entry in dictionaries)
                    w.Line(entry + ",");
            }
        }

        var survey = new CodeWriter();
        Output.Header(survey, nullable: false);
        survey.Line(w.ToString().TrimEnd('\n'));

        return new CompiledDocument("registry")
        {
            HintName = "XamlCompileSurvey.g.cs",
            Source = survey.ToString(),
        };
    }

    /// <summary>Runs the emitter over one document and renders whatever it earned the right to
    /// emit. Called once per document; both source outputs read the result.</summary>
    static CompiledDocument CompileDocument(
        string path,
        string text,
        Compilation compilation,
        XamlCompilerOptions options,
        bool runtime
    )
    {
        var document = new CompiledDocument(System.IO.Path.GetFileName(path));

        XDocument parsed;
        try
        {
            parsed = XDocument.Parse(text, LoadOptions.None);
        }
        catch (Exception ex)
        {
            document.Errors = [$"XML parse failed: {ex.Message}"];
            return document;
        }

        if (parsed.Root is not { } root)
            return document;

        var prefix = options.PrefixFor(compilation);
        var resolver = new XamlTypeResolver(compilation, options);
        var emitter = new XamlEmitter(resolver, prefix, options.ProjectDir, path)
        {
            CompiledBindingsAvailable = runtime,
        };
        var typeName = XamlPaths.TypeNameFor(path, options.ProjectDir);

        var (isDictionary, rootClass) = Classify(resolver, root);
        if (!isDictionary && rootClass is null)
        {
            document.Errors = [NoRootClass];
            return document;
        }

        document.HintName = typeName + ".g.cs";
        document.Logical = XamlPaths.LogicalName(path, prefix, options.ProjectDir);

        var w = new CodeWriter();
        Output.Header(w, nullable: false);

        if (rootClass is not null)
        {
            EmitRootClass(w, root, rootClass, resolver, emitter, compilation, document);
        }
        else
        {
            var body = emitter.EmitDictionary(root);
            document.Gated = true;

            using (w.Block($"namespace {GeneratedNamespace(compilation)}"))
            {
                w.Line($"using __XamlResources = {GeneratedNamespace(compilation)}.XamlResources;");
                using (w.Block($"public static class {typeName}"))
                using (w.Block("public static global::Noesis.ResourceDictionary Build()"))
                {
                    foreach (var line in body)
                        w.Line(line);
                }
            }
        }

        document.Source = w.ToString();
        document.Tally = emitter.Tally;
        document.Errors = emitter.Errors;
        document.DeadMarkup = emitter.DeadMarkup;

        if (isDictionary && emitter.Errors.Count == 0)
        {
            document.DictionaryRow =
                $"(@\"{XamlPaths.RelativePath(path, options.ProjectDir)}\", @\"{document.Logical}\", "
                + $"() => {typeName}.Build())";
            document.DeclaredKeys = emitter.DeclaredKeys(root).ToList();
        }

        return document;
    }

    static (bool IsDictionary, INamedTypeSymbol? RootClass) Classify(
        XamlTypeResolver resolver,
        XElement root
    )
    {
        if (ClassType(resolver, root) is { } rootClass)
            return (false, rootClass);

        var dictionary =
            root.Name.LocalName == "ResourceDictionary"
            || (
                resolver.SymbolOf(root) is { } symbol
                && XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.ResourceDictionary")
            );

        return (dictionary, null);
    }

    // A managed handler pins the root through the child element; only the loader owns that lifetime.
    static void EmitRootClass(
        CodeWriter w,
        XElement root,
        INamedTypeSymbol rootClass,
        XamlTypeResolver resolver,
        XamlEmitter emitter,
        Compilation compilation,
        CompiledDocument document
    )
    {
        var scan = XamlCodeBehind.Read(root, resolver);
        if (XamlCodeBehind.NameConflict(scan, rootClass) is { } conflict)
        {
            document.Errors = [conflict];
            document.Gated = true;
            return;
        }

        var compiled = !scan.NeedsLoader;
        var body = compiled ? emitter.EmitRoot(root, rootClass) : null;

        document.NeedsLoader = scan.NeedsLoader;
        document.Gated = compiled;

        if (rootClass.NamespaceOrNull() is { } ns)
            w.Line($"namespace {ns};");

        w.Line($"using __XamlResources = {GeneratedNamespace(compilation)}.XamlResources;");
        w.Line();

        var scopes = PartialTypeEmitter.Open(w, PartialTypeEmitter.DeclarationChain(rootClass));

        XamlCodeBehind.Emit(w, rootClass, scan, document.Logical!, compiled);

        if (body is not null)
        {
            w.Line();
            using (w.Block("internal void BuildXamlTree()"))
            {
                foreach (var line in body)
                    w.Line(line);
            }
        }

        PartialTypeEmitter.Close(scopes);
    }

    static string GeneratedNamespace(Compilation compilation) =>
        GeneratedNamespace(compilation.AssemblyName ?? "Assembly");

    static string GeneratedNamespace(string assemblyName)
    {
        var name = new string(assemblyName.Where(char.IsLetterOrDigit).ToArray());
        return "XamlGenerated." + (name.Length == 0 || char.IsDigit(name[0]) ? "_" + name : name);
    }

    static INamedTypeSymbol? ClassType(XamlTypeResolver resolver, XElement root)
    {
        var attribute = root.Attribute(XName.Get("Class", XamlTypeResolver.DirectiveNs));
        if (attribute is null)
            return null;

        var name = attribute.Value;
        var dot = name.LastIndexOf('.');
        return dot <= 0
            ? null
            : resolver.Resolve("clr-namespace:" + name.Substring(0, dot), name.Substring(dot + 1));
    }
}
