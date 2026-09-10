using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NoesisToolkit.Tests;

sealed record GeneratorRun(
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompileDiagnostics,
    IReadOnlyDictionary<string, string> Sources
)
{
    public IEnumerable<Diagnostic> Errors =>
        GeneratorDiagnostics
            .Concat(CompileDiagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error);

    public string Source(string hintNameFragment) =>
        Sources.First(s => s.Key.Contains(hintNameFragment, StringComparison.Ordinal)).Value;

    public string AllSources => string.Join("\n", Sources.Values);
}

static class GeneratorHarness
{
    // Its own assembly, not a syntax tree: the implicit-style key depends on the declaring assembly.
    static readonly CSharpCompilation StubCompilation = BuildStub();

    public static readonly MetadataReference NoesisReference =
        StubCompilation.ToMetadataReference();

    static CSharpCompilation BuildStub()
    {
        var compilation = CSharpCompilation.Create(
            "Noesis.GUI",
            [CSharpSyntaxTree.ParseText(Embedded("NoesisStub.cs.txt"), Parse)],
            Basic.Reference.Assemblies.Net80.References.All,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException(
                "NoesisStub.cs.txt does not compile: "
                    + string.Join("; ", errors.Select(d => d.ToString()))
            );

        return compilation;
    }

    static readonly CSharpParseOptions Parse = new(LanguageVersion.Preview);

    /// <summary>Compiles <paramref name="source"/> into a reference named
    /// <paramref name="assemblyName"/>, for a test whose subject is the referenced-assembly graph
    /// rather than one compilation.</summary>
    public static MetadataReference Reference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, Parse)],
            [.. Basic.Reference.Assemblies.Net80.References.All, NoesisReference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics));

        stream.Position = 0;
        return MetadataReference.CreateFromStream(stream);
    }

    static CSharpCompilation Compile(
        string assemblyName,
        IEnumerable<SyntaxTree> trees,
        bool referenceNoesis = true,
        IEnumerable<MetadataReference>? extraReferences = null
    )
    {
        List<MetadataReference> references = [.. Basic.Reference.Assemblies.Net80.References.All];
        if (referenceNoesis)
            references.Add(NoesisReference);

        references.AddRange(extraReferences ?? []);

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
    }

    static List<SyntaxTree> Trees(IEnumerable<string>? sources) =>
        (sources ?? []).Select(s => CSharpSyntaxTree.ParseText(s, Parse)).ToList();

    static ImmutableArray<AdditionalText> Additional(IEnumerable<string> fixtures) =>
        fixtures
            .Select(name =>
                (AdditionalText)
                    new FixtureText(
                        Path.Combine("/repo/App", name),
                        Embedded(Path.Combine("Fixtures", name))
                    )
            )
            .ToImmutableArray();

    /// <summary>
    /// Compiles the stub Noesis surface plus <paramref name="extraSources"/>, runs the generator over
    /// the named fixtures, then compiles the result: emitted C# that does not bind is a test failure.
    /// </summary>
    public static GeneratorRun Run(
        IIncrementalGenerator generator,
        IEnumerable<string> fixtures,
        IEnumerable<string>? extraSources = null,
        IDictionary<string, string>? buildProperties = null,
        string assemblyName = "SampleApp",
        bool referenceNoesis = true,
        IEnumerable<MetadataReference>? extraReferences = null
    ) =>
        Run(
            [generator],
            fixtures,
            extraSources,
            buildProperties,
            assemblyName,
            referenceNoesis,
            extraReferences
        );

    /// <summary>
    /// The same, with more than one generator in the pass. Each still sees only the compilation the
    /// driver started from, so what one emits is another's to infer rather than look up.
    /// </summary>
    public static GeneratorRun Run(
        IIncrementalGenerator[] generators,
        IEnumerable<string> fixtures,
        IEnumerable<string>? extraSources = null,
        IDictionary<string, string>? buildProperties = null,
        string assemblyName = "SampleApp",
        bool referenceNoesis = true,
        IEnumerable<MetadataReference>? extraReferences = null
    )
    {
        var parse = Parse;
        var trees = Trees(extraSources);
        var compilation = Compile(assemblyName, trees, referenceNoesis, extraReferences);
        var additional = Additional(fixtures);

        var driver = CSharpGeneratorDriver
            .Create(generators.Select(g => g.AsSourceGenerator()).ToArray(), parseOptions: parse)
            .AddAdditionalTexts(additional)
            .WithUpdatedAnalyzerConfigOptions(new OptionsProvider(buildProperties));

        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generatorDiagnostics
        );

        var sources = output
            .SyntaxTrees.Skip(trees.Count)
            .ToDictionary(t => Path.GetFileName(t.FilePath), t => t.ToString());

        return new GeneratorRun(generatorDiagnostics, output.GetDiagnostics(), sources);
    }

    /// <summary>
    /// Runs the driver twice, adding one unrelated source between runs, and returns why each tracked
    /// step re-ran. A step that is not Cached or Unchanged means the pipeline lost its caching.
    /// </summary>
    public static IReadOnlyList<IncrementalStepRunReason> RunAfterUnrelatedEdit(
        IIncrementalGenerator generator,
        IEnumerable<string> fixtures,
        IEnumerable<string>? extraSources,
        IDictionary<string, string>? buildProperties,
        IReadOnlyCollection<string> trackedSteps
    )
    {
        var parse = Parse;
        var compilation = Compile("SampleApp", Trees(extraSources));
        var additional = Additional(fixtures);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            additionalTexts: additional,
            parseOptions: parse,
            optionsProvider: new OptionsProvider(buildProperties),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true
            )
        );

        driver = driver.RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Sample { class Unrelated { } }", parse)
        );

        return driver
            .RunGenerators(edited)
            .GetRunResult()
            .Results.SelectMany(r => r.TrackedSteps)
            .Where(s => trackedSteps.Contains(s.Key))
            .SelectMany(s => s.Value)
            .SelectMany(s => s.Outputs)
            .Select(o => o.Reason)
            .ToList();
    }

    /// <summary>
    /// Emits the fixture compilation plus its generated sources and loads the result, so a test can
    /// call the generated code. The stub's ResourceDictionary is a working in-memory implementation,
    /// which is what makes the resource-timing invariants observable.
    /// </summary>
    public static Assembly Execute(
        IIncrementalGenerator generator,
        IEnumerable<string> fixtures,
        IDictionary<string, string>? buildProperties = null
    )
    {
        var parse = Parse;
        var compilation = Compile("SampleApp", []);
        var additional = Additional(fixtures);

        CSharpGeneratorDriver
            .Create([generator.AsSourceGenerator()], parseOptions: parse)
            .AddAdditionalTexts(additional)
            .WithUpdatedAnalyzerConfigOptions(new OptionsProvider(buildProperties))
            .RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out var generatorDiagnostics
            );

        using var stubStream = new MemoryStream();
        Succeeded(StubCompilation.Emit(stubStream), "the Noesis stub", []);
        using var stream = new MemoryStream();
        Succeeded(output.Emit(stream), "the generated assembly", generatorDiagnostics);

        var context = new AssemblyLoadContext("fixtures", isCollectible: true);
        stubStream.Position = 0;
        context.LoadFromStream(stubStream);
        stream.Position = 0;
        return context.LoadFromStream(stream);
    }

    static void Succeeded(
        Microsoft.CodeAnalysis.Emit.EmitResult result,
        string what,
        ImmutableArray<Diagnostic> generatorDiagnostics
    )
    {
        if (result.Success)
            return;

        var reported = generatorDiagnostics
            .Concat(result.Diagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());

        throw new InvalidOperationException(
            $"could not emit {what}: " + string.Join("; ", reported)
        );
    }

    static string Embedded(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
}
