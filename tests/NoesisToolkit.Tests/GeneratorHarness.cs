using System.Collections.Immutable;
using System.Linq;
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
    static readonly string[] ReferencePaths = File.ReadAllLines(
        Path.Combine(AppContext.BaseDirectory, "harness.references")
    );

    public static readonly MetadataReference[] Framework = ReferencePaths
        .Where(p => Path.GetFileName(p) is not ("Noesis.GUI.dll" or "NoesisToolkit.Mvvm.dll"))
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    public static readonly MetadataReference NoesisReference = Only("Noesis.GUI.dll");

    public static readonly MetadataReference MvvmReference = Only("NoesisToolkit.Mvvm.dll");

    static MetadataReference Only(string file) =>
        MetadataReference.CreateFromFile(ReferencePaths.Single(p => Path.GetFileName(p) == file));

    static readonly CSharpParseOptions Parse = new(LanguageVersion.Preview);

    /// <summary>Compiles <paramref name="source"/> into a reference named
    /// <paramref name="assemblyName"/>, for a test whose subject is the referenced-assembly graph
    /// rather than one compilation.</summary>
    public static MetadataReference Reference(string assemblyName, string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, Parse)],
            [.. Framework, NoesisReference],
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
        bool referenceMvvm = false,
        IEnumerable<MetadataReference>? extraReferences = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary
    )
    {
        List<MetadataReference> references = [.. Framework];
        if (referenceNoesis)
            references.Add(NoesisReference);

        if (referenceMvvm)
            references.Add(MvvmReference);

        references.AddRange(extraReferences ?? []);

        return CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(
                outputKind,
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
    /// Compiles <paramref name="extraSources"/> against Noesis.GUI, runs the generator over the named
    /// fixtures, then compiles the result: emitted C# that does not bind is a test failure.
    /// </summary>
    public static GeneratorRun Run(
        IIncrementalGenerator generator,
        IEnumerable<string> fixtures,
        IEnumerable<string>? extraSources = null,
        IDictionary<string, string>? buildProperties = null,
        string assemblyName = "SampleApp",
        bool referenceNoesis = true,
        IEnumerable<MetadataReference>? extraReferences = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool referenceMvvm = false
    ) =>
        Run(
            [generator],
            fixtures,
            extraSources,
            buildProperties,
            assemblyName,
            referenceNoesis,
            extraReferences,
            outputKind,
            referenceMvvm
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
        IEnumerable<MetadataReference>? extraReferences = null,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        bool referenceMvvm = false
    )
    {
        var parse = Parse;
        var trees = Trees(extraSources);
        var compilation = Compile(
            assemblyName,
            trees,
            referenceNoesis,
            referenceMvvm,
            extraReferences,
            outputKind
        );
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
        IReadOnlyCollection<string> trackedSteps,
        bool referenceMvvm = false
    )
    {
        var parse = Parse;
        var compilation = Compile("SampleApp", Trees(extraSources), referenceMvvm: referenceMvvm);
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

    static string Embedded(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
}
