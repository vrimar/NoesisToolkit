using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
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

    static readonly DiagnosticDescriptor Unrootable = new(
        "NTK1004",
        "A name left to the engine cannot be rooted for trimming",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The engine resolves a binding it was left, and a name the document hands it as "
            + "text, by reflection at run time. A trimmed build keeps only what the compiler can "
            + "name, and it cannot name a member of a type it cannot determine."
    );

    static readonly DiagnosticDescriptor LeftToLoader = new(
        "NTK1005",
        "A document is parsed at run time",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every other document is built in code, so an app can ship without its XAML. "
            + "This one stays on GUI.LoadComponent, which parses the document's source at run time, "
            + "so that source has to ship with the app."
    );

    static readonly DiagnosticDescriptor UnresolvedType = new(
        "NTK1006",
        "A type the document states does not resolve",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor AbsentName = new(
        "NTK1008",
        "A name the engine is handed as text does not exist",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The engine looks up a DisplayMemberPath, SelectedValuePath or EventName by name "
            + "at run time and finds nothing when the type it reads does not have that member, in "
            + "every build."
    );

    static readonly DiagnosticDescriptor LeftNative = new(
        "NTK1007",
        "A binding stays native",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "The engine resolves a binding or trigger it is left by reflection at run "
            + "time, so every name the markup reads has to ship. Raised where the compiler leaves "
            + "one to the engine, with the reason, for an audit of what is left."
    );

    static readonly DiagnosticDescriptor NativeCell = new(
        "NTK1010",
        "A list column's DisplayMemberBinding stays native",
        "{0}",
        "NoesisToolkit",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The row presenter applies a DisplayMemberBinding to a text block it makes "
            + "itself, so the binding cannot be compiled: the engine reads its path by reflection and "
            + "boxes the value for every row it fills, again each time a recycling list reuses the "
            + "row. A CellTemplate is typed off the list's items and compiles."
    );

    static readonly DiagnosticDescriptor OtherRuntime = new(
        "NTK1011",
        "NoesisToolkit.Mvvm is another version than the XAML compiler",
        "NoesisToolkit.Mvvm {0} is referenced, but the XAML compiler is {1}; every binding stays native until both are the same version",
        "NoesisToolkit",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A compiled binding is emitted against the runtime API of the NoesisToolkit.Mvvm "
            + "release the compiler shipped with, and that API changes from release to release. "
            + "Against another version the generated code would not build, so the compiler leaves "
            + "every binding to the engine, as it does when the package is absent."
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

    internal const string CompiledStep = "NoesisXamlCompiled";

    internal const string RegistryStep = "NoesisXamlRegistry";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var options = context
            .AnalyzerConfigOptionsProvider.Select((o, _) => XamlCompilerOptions.Read(o))
            .WithTrackingName(OptionsStep);

        var files = context
            .AdditionalTextsProvider.Combine(options)
            .Where(pair => pair.Right.Matches(pair.Left.Path))
            .Select((pair, _) => pair.Left)
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(
                (pair, ct) =>
                    (
                        pair.Left.Path,
                        Text: pair.Left.GetText(ct)?.ToString(),
                        Link: LinkOf(pair.Right.GetOptions(pair.Left))
                    )
            )
            .WithTrackingName(DocumentsStep);

        var compiled = files
            .Combine(context.CompilationProvider)
            .Combine(options)
            .Select(
                (pair, ct) =>
                {
                    var (((path, text, link), compilation), opts) = pair;
                    if (
                        string.IsNullOrWhiteSpace(text)
                        || compilation.GetTypeByMetadataName("Noesis.GUI") is null
                    )
                        return null;

                    // The emitted graph names the runtime by string. Without it the generated file
                    // would not compile, so a project that installed the compiler alone gets the
                    // native parser rather than a build error.
                    var runtime =
                        compilation.GetTypeByMetadataName(RuntimeType) is not null
                        && RuntimeOtherThanOwn(compilation) is null;

                    return Guarded(
                        path,
                        () => CompileDocument(path, link, text!, compilation, opts, runtime, ct)
                    );
                }
            )
            .WithTrackingName(CompiledStep);

        var scope = context.CompilationProvider.Select((compilation, _) => ScopeOf(compilation));

        context.RegisterSourceOutput(
            compiled.Collect().Combine(scope).WithTrackingName(RegistryStep),
            (spc, pair) =>
            {
                var (all, registryScope) = pair;
                var documents = all.OfType<CompiledDocument>().ToList();
                if (documents.Count == 0)
                    return;

                if (registryScope.OtherRuntime is { } other)
                    spc.ReportDiagnostic(
                        Diagnostic.Create(OtherRuntime, null, other, CompilerVersion)
                    );

                var registry = Guarded("registry", () => EmitRegistry(documents, registryScope));
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

        for (var i = 0; i < document.Errors.Count; i++)
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    Unsupported,
                    i < document.ErrorSites.Count ? document.ErrorSites[i]?.ToLocation() : null,
                    $"{document.File} :: {document.Errors[i]}"
                )
            );

        foreach (var unrooted in document.Unrooted)
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    Unrootable,
                    unrooted.Location?.ToLocation(),
                    document.UnrootedSeverity,
                    additionalLocations: null,
                    properties: null,
                    $"{document.File} :: {unrooted.Message}"
                )
            );

        foreach (var absent in document.Absent)
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    AbsentName,
                    absent.Location?.ToLocation(),
                    $"{document.File} :: {absent.Message}"
                )
            );

        foreach (var unresolved in document.UnresolvedTypes)
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    UnresolvedType,
                    unresolved.Location?.ToLocation(),
                    $"{document.File} :: {unresolved.Message}"
                )
            );

        foreach (var native in document.LeftNative)
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    LeftNative,
                    native.Location?.ToLocation(),
                    $"{document.File} :: {native.Message}"
                )
            );

        foreach (var cell in document.NativeCells)
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    NativeCell,
                    cell.Location?.ToLocation(),
                    $"{document.File} :: {cell.Message}"
                )
            );

        if (document.Loader is { } loader)
            spc.ReportDiagnostic(
                Diagnostic.Create(
                    LeftToLoader,
                    loader.Location?.ToLocation(),
                    $"{document.File} :: {loader.Message}"
                )
            );

        if (document.Source is null || (document.Gated && document.Errors.Count > 0))
            return;

        Output.Add(spc, document.HintName!, document.Source);
    }

    // Read off the compilation apart from it, so an edit that leaves these alone leaves the registry cached.
    sealed class RegistryScope(
        string generated,
        string self,
        IReadOnlyList<string> chain,
        string? otherRuntime
    ) : IEquatable<RegistryScope>
    {
        public string Generated { get; } = generated;

        public string Self { get; } = self;

        public IReadOnlyList<string> Chain { get; } = chain;

        public string? OtherRuntime { get; } = otherRuntime;

        public bool Equals(RegistryScope? other) =>
            other is not null
            && Generated == other.Generated
            && Self == other.Self
            && Chain.SequenceEqual(other.Chain)
            && OtherRuntime == other.OtherRuntime;

        public override bool Equals(object? obj) => Equals(obj as RegistryScope);

        public override int GetHashCode() => (Generated.GetHashCode() * 397) ^ Chain.Count;
    }

    static RegistryScope ScopeOf(Compilation compilation)
    {
        var chain = new List<string>();
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var linked = LinkName(reference.Name);
            if (compilation.GetTypeByMetadataName(linked) is not null)
            {
                chain.Add("global::" + linked);
                continue;
            }

            var candidate = GeneratedNamespace(reference.Name) + ".XamlResources";
            var link = "global::" + candidate;
            if (compilation.GetTypeByMetadataName(candidate) is null || chain.Contains(link))
                continue;

            chain.Add(link);
        }

        return new RegistryScope(
            GeneratedNamespace(compilation),
            LinkName(compilation.AssemblyName ?? "Assembly"),
            chain,
            RuntimeOtherThanOwn(compilation)?.ToString()
        );
    }

    const string RuntimeType = "NoesisToolkit.Mvvm.CodeGen.CompiledBinding";

    // A release builds every package at one version, so only the runtime cut with this compiler matches.
    static readonly Version CompilerVersion = typeof(XamlCompileGenerator)
        .Assembly.GetName()
        .Version!;

    static Version? RuntimeOtherThanOwn(Compilation compilation) =>
        compilation.GetTypeByMetadataName(RuntimeType)?.ContainingAssembly is { } runtime
        && !SymbolEqualityComparer.Default.Equals(runtime, compilation.Assembly)
        && runtime.Identity.Version != CompilerVersion
            ? runtime.Identity.Version
            : null;

    static CompiledDocument EmitRegistry(IReadOnlyList<CompiledDocument> all, RegistryScope scope)
    {
        var w = new CodeWriter();
        var ok = 0;
        var fail = 0;
        var loaders = 0;
        var rows = new List<string>();
        var tally = new BindingTally();

        var dictionaries = new List<string>();
        var errors = new List<string>();
        var errorSites = new List<LocationInfo?>();
        foreach (var claimed in all.Where(d => d.RootClass is not null).GroupBy(d => d.RootClass))
        {
            if (claimed.Count() < 2)
                continue;

            errors.Add(
                $"x:Class '{claimed.Key}' is declared by "
                    + string.Join(" and ", claimed.Select(d => d.File))
                    + "; each partial class takes one document"
            );
            errorSites.Add(claimed.Skip(1).First().ClassSite);
        }

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
                else
                    keyLeaves.Remove(key);
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
        var chain = scope.Chain;
        var generated = scope.Generated;
        var own = generated + ".XamlResources";
        var self = scope.Self;
        var dot = self.LastIndexOf('.');
        using (w.Block($"namespace {self.Substring(0, dot)}"))
        {
            w.Line(
                "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]"
            );
            using (w.Block($"public static class {self.Substring(dot + 1)}"))
            {
                w.Line(
                    $"public static void Flush(global::Noesis.ResourceDictionary root) => global::{own}.Flush(root);"
                );
                w.Line(
                    $"public static global::Noesis.ResourceDictionary Resolve(string logical) => global::{own}.Resolve(logical);"
                );
                w.Line(
                    $"public static object SharedByKey(object key) => global::{own}.SharedByKey(key);"
                );
            }
        }

        using (w.Block($"namespace {generated}"))
        using (w.Block("public static class XamlResources"))
        {
            w.Line(
                "static readonly global::System.Collections.Generic.List<global::System.Action<global::Noesis.ResourceDictionary>> __pending = new();"
            );
            w.Line("static global::Noesis.ResourceDictionary __flushing;");
            w.Line("static bool __flushed;");

            // Built after the flush: nothing of its own is sealed, and no drain is coming.
            using (
                w.Block(
                    "public static void Defer(global::System.Action<global::Noesis.ResourceDictionary> fixup)"
                )
            )
            {
                w.Line("if (__flushing != null) { fixup(__flushing); return; }");
                // Not the flushed root: a reload installs a new graph and the parser reads that one.
                w.Line(
                    "if (__flushed) { fixup(global::Noesis.GUI.GetApplicationResources()); return; }"
                );
                w.Line("__pending.Add(fixup);");
            }

            using (w.Block("public static void Flush(global::Noesis.ResourceDictionary root)"))
            {
                w.Line("__flushing = root;");
                using (w.Block("try"))
                {
                    // Indexed: settling one entry can build another dictionary, which appends.
                    w.Line("for (var i = 0; i < __pending.Count; i++) __pending[i](root);");
                    w.Line("__pending.Clear();");
                }
                using (w.Block("finally"))
                {
                    w.Line("__flushing = null;");
                }
                w.Line("__flushed = true;");
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

            using (
                w.Block(
                    "internal sealed class MarkupProvider : global::System.IServiceProvider, global::Noesis.IProvideValueTarget, global::Noesis.IUriContext"
                )
            )
            {
                w.Line(
                    "public MarkupProvider(object target, object property, global::System.Uri baseUri) { TargetObject = target; TargetProperty = property; BaseUri = baseUri; }"
                );
                w.Line("public object TargetObject { get; }");
                w.Line("public object TargetProperty { get; }");
                w.Line("public global::System.Uri BaseUri { get; }");
                w.Line(
                    "public object GetService(global::System.Type serviceType) => serviceType == typeof(global::Noesis.IProvideValueTarget) || serviceType == typeof(global::Noesis.IUriContext) ? this : null;"
                );
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
                    w.Line($"[{owner.Key}] = {XamlEmitter.Quote(owner.Value)},");
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

        using (w.Block($"namespace {generated}"))
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
            Errors = errors,
            ErrorSites = errorSites,
        };
    }

    /// <summary>Runs the emitter over one document and renders whatever it earned the right to
    /// emit. Called once per document; both source outputs read the result.</summary>
    static string LinkOf(Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptions options) =>
        options.TryGetValue("build_metadata.AdditionalFiles.Link", out var link)
        && !string.IsNullOrWhiteSpace(link)
            ? link.Trim()
            : "";

    static CompiledDocument CompileDocument(
        string path,
        string link,
        string text,
        Compilation compilation,
        XamlCompilerOptions options,
        bool runtime,
        System.Threading.CancellationToken cancellation
    )
    {
        // A linked file is named by where the project places it, not by where it lives on disk.
        var placed =
            link.Length > 0 && options.ProjectDir.Length > 0
                ? options.ProjectDir.TrimEnd('/', '\\') + "/" + link
                : path;
        var document = new CompiledDocument(XamlPaths.RelativePath(placed, options.ProjectDir));

        XDocument parsed;
        try
        {
            parsed = XDocument.Parse(
                text,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo
            );
        }
        catch (Exception ex)
        {
            document.Errors = [$"XML parse failed: {ex.Message}"];
            if (ex is XmlException { LineNumber: > 0 } xml)
                document.ErrorSites = [LocationIn(path, text, xml.LineNumber, xml.LinePosition)];
            return document;
        }

        if (parsed.Root is not { } root)
            return document;

        var prefix = options.PrefixFor(compilation);
        var resolver = new XamlTypeResolver(compilation, options);
        var emitter = new XamlEmitter(resolver, prefix, options.ProjectDir, placed)
        {
            MvvmRuntimeReferenced = runtime,
            Cancellation = cancellation,
        };
        var typeName = XamlPaths.TypeNameFor(placed, options.ProjectDir);

        var (isDictionary, rootClass) = Classify(resolver, root);
        var declared = root.Attribute(XName.Get("Class", XamlTypeResolver.DirectiveNs));
        if (rootClass is null && (!isDictionary || declared is not null))
        {
            document.Errors =
            [
                declared is null
                    ? NoRootClass
                    : $"x:Class '{declared.Value.Trim()}' does not resolve; declare it as a partial class",
            ];
            document.ErrorSites = [LocationIn(path, text, (IXmlLineInfo?)declared ?? root)];
            return document;
        }

        document.RootClass = rootClass is null ? null : XamlTypeResolver.Fqn(rootClass);
        document.ClassSite = LocationIn(
            path,
            text,
            root.Attribute(XName.Get("Class", XamlTypeResolver.DirectiveNs)) ?? (IXmlLineInfo)root
        );

        document.HintName = XamlPaths.HintNameFor(typeName);
        document.Logical = XamlPaths.LogicalName(placed, prefix, options.ProjectDir);

        var w = new CodeWriter();
        Output.Header(w, nullable: false);
        var generated = GeneratedNamespace(compilation);

        if (rootClass is not null)
        {
            var scan = EmitRootClass(
                w,
                root,
                rootClass,
                resolver,
                emitter,
                compilation,
                generated,
                document
            );
            if (scan?.LoaderAttribute is { } handler)
                document.Loader = new MarkupDiagnostic(
                    $"{handler.Name.LocalName}=\"{handler.Value}\" wires a handler in markup, so "
                        + "GUI.LoadComponent parses this document at run time and its source has to "
                        + "ship; subscribe the handler in code to build the document in code",
                    LocationIn(path, text, handler)
                );
            else if (scan?.LiveResource is { } live)
                document.Loader = new MarkupDiagnostic(
                    $"{live.Name.LocalName}=\"{live.Value}\" on the root's own <{live.Parent!.Name.LocalName}> "
                        + "stays live only when the parser builds it, so GUI.LoadComponent parses this "
                        + "document at run time and its source has to ship; move the reference onto an "
                        + "element to build the document in code",
                    LocationIn(path, text, live)
                );
        }
        else
        {
            var body = emitter.EmitDictionary(root);
            emitter.RootReferencedTypes(root);
            document.Gated = true;

            using (w.Block($"namespace {generated}"))
            {
                w.Line($"using __XamlResources = {generated}.XamlResources;");
                using (w.Block($"public static class {typeName}"))
                {
                    foreach (var attribute in emitter.Roots.Render(compilation))
                        w.Line(attribute);

                    using (w.Block("public static global::Noesis.ResourceDictionary Build()"))
                    {
                        foreach (var line in body)
                            w.Line(line);
                    }
                }
            }
        }

        document.Source = w.ToString();
        document.Tally = emitter.Tally;
        document.Errors = emitter.Errors.Messages.ToList();
        document.ErrorSites = emitter
            .Errors.Sites.Select(s => LocationIn(path, text, s.Line, s.Column))
            .ToList();
        document.DeadMarkup = emitter.DeadMarkup.ToList();
        document.UnresolvedTypes = emitter
            .UnresolvedTypes.Distinct()
            .Select(a => new MarkupDiagnostic(
                $"ntk:{a.Name.LocalName}=\"{a.Value}\" names no type the compiler can find; check "
                    + "the prefix's clr-namespace and assembly",
                LocationIn(path, text, a)
            ))
            .ToList();
        document.LeftNative = emitter
            .LeftNative.Select(n => new MarkupDiagnostic(
                n.Message,
                LocationIn(path, text, n.Line, n.Column)
            ))
            .ToList();
        document.NativeCells = emitter
            .NativeCells.Select(n => new MarkupDiagnostic(
                n.Message,
                LocationIn(path, text, n.Line, n.Column)
            ))
            .ToList();
        document.Unrooted = emitter
            .Unrooted.Distinct()
            .Select(u => new MarkupDiagnostic(u.Message, LocationIn(path, text, u.Line, u.Column)))
            .ToList();
        document.Absent = emitter
            .Absent.Distinct()
            .Select(a => new MarkupDiagnostic(a.Message, LocationIn(path, text, a.Line, a.Column)))
            .ToList();
        // A library is trimmed by whichever app consumes it, and no step there checks these names.
        document.UnrootedSeverity =
            options.Trimmed
            || compilation.Options.OutputKind
                is not (
                    OutputKind.ConsoleApplication
                    or OutputKind.WindowsApplication
                    or OutputKind.WindowsRuntimeApplication
                )
                ? DiagnosticSeverity.Warning
                : DiagnosticSeverity.Info;

        if (isDictionary && emitter.Errors.Count == 0)
        {
            document.DictionaryRow =
                $"({XamlEmitter.Quote(XamlPaths.RelativePath(placed, options.ProjectDir))}, {XamlEmitter.Quote(document.Logical!)}, "
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
    static XamlCodeBehind.Scan? EmitRootClass(
        CodeWriter w,
        XElement root,
        INamedTypeSymbol rootClass,
        XamlTypeResolver resolver,
        XamlEmitter emitter,
        Compilation compilation,
        string generated,
        CompiledDocument document
    )
    {
        var scan = XamlCodeBehind.Read(root, rootClass, resolver);
        var collisions = XamlCodeBehind.Collisions(scan, rootClass).ToList();
        if (XamlCodeBehind.NameConflict(scan, rootClass) is { } conflict)
            collisions.Insert(0, conflict);

        if (collisions.Count > 0)
        {
            emitter.Errors.AddRange(collisions);
            document.Gated = true;
            return null;
        }

        emitter.Errors.AddRange(XamlCodeBehind.Unwired(scan));
        emitter.Errors.AddRange(scan.Problems);

        var compiled = !scan.NeedsLoader;
        emitter.HideRootMembers(
            scan.Names.Select(n => n.Name).Where(n => XamlCodeBehind.Inherits(rootClass, n))
        );
        var body = compiled ? emitter.EmitRoot(root, rootClass) : null;
        if (!compiled)
            emitter.RootLoaded(root, rootClass, scan.Events.Count > 0);

        emitter.RootReferencedTypes(root);
        var roots = emitter.Roots.Render(compilation);

        document.NeedsLoader = scan.NeedsLoader;
        document.Gated = compiled;

        if (rootClass.NamespaceOrNull() is { } ns)
            w.Line($"namespace {ns};");

        w.Line($"using __XamlResources = {generated}.XamlResources;");
        w.Line();

        var scopes = PartialTypeEmitter.Open(w, PartialTypeEmitter.DeclarationChain(rootClass));

        XamlCodeBehind.Emit(w, rootClass, scan, document.Logical!, compiled, compiled ? [] : roots);

        if (body is not null)
        {
            w.Line();
            foreach (var attribute in roots)
                w.Line(attribute);

            using (w.Block("internal void BuildXamlTree()"))
            {
                foreach (var line in body)
                    w.Line(line);
            }
        }

        PartialTypeEmitter.Close(scopes);
        return scan;
    }

    static LocationInfo? LocationIn(string path, string text, IXmlLineInfo at) =>
        at.HasLineInfo() ? LocationIn(path, text, at.LineNumber, at.LinePosition) : null;

    // Line information is 1-based and counts a tab as one column, as a text span does.
    static LocationInfo? LocationIn(string path, string text, int lineNumber, int column)
    {
        if (lineNumber <= 0)
            return null;

        var offset = 0;
        for (var line = 1; line < lineNumber && offset >= 0; line++)
            offset = text.IndexOf('\n', offset) is var next and >= 0 ? next + 1 : -1;

        if (offset < 0)
            return null;

        var start = Math.Min(offset + column - 1, text.Length);
        var length = 0;
        while (
            start + length < text.Length
            && (
                System.Xml.XmlConvert.IsNCNameChar(text[start + length])
                || text[start + length] == ':'
            )
        )
            length++;
        var position = new LinePosition(lineNumber - 1, column - 1);
        return new LocationInfo(
            path,
            new TextSpan(start, length),
            new LinePositionSpan(
                position,
                new LinePosition(position.Line, position.Character + length)
            )
        );
    }

    static string GeneratedNamespace(Compilation compilation)
    {
        var assemblyName = compilation.AssemblyName ?? "Assembly";
        var plain = GeneratedNamespace(assemblyName);
        return compilation.SourceModule.ReferencedAssemblySymbols.Any(r =>
            r.GetTypeByMetadataName(plain + ".XamlResources") is not null
        )
            ? "XamlGenerated." + SpelledOut(assemblyName)
            : plain;
    }

    static string GeneratedNamespace(string assemblyName)
    {
        var name = new string(assemblyName.Where(char.IsLetterOrDigit).ToArray());
        return "XamlGenerated." + (name.Length == 0 || char.IsDigit(name[0]) ? "_" + name : name);
    }

    static string LinkName(string assemblyName) =>
        "XamlGenerated.Links." + SpelledOut(assemblyName);

    // Spelling out every other character keeps two assembly names from sharing one name.
    static string SpelledOut(string assemblyName)
    {
        var name = new System.Text.StringBuilder("A_");
        foreach (var c in assemblyName)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                name.Append(c);
            else
                name.Append("_x")
                    .Append(
                        ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)
                    )
                    .Append('_');
        }

        return name.ToString();
    }

    static INamedTypeSymbol? ClassType(XamlTypeResolver resolver, XElement root)
    {
        var attribute = root.Attribute(XName.Get("Class", XamlTypeResolver.DirectiveNs));
        if (attribute is null)
            return null;

        var name = attribute.Value.Trim();
        var dot = name.LastIndexOf('.');
        return name.Length == 0 || dot == 0
            ? null
            : resolver.Resolve(
                "clr-namespace:" + (dot < 0 ? "" : name.Substring(0, dot)),
                name.Substring(dot + 1)
            );
    }
}
