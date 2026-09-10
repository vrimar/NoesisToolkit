using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NoesisToolkit.Tests;

static class AnalyzerHarness
{
    /// <summary>Runs an analyzer over one XAML document, against a compilation of the given sources.</summary>
    public static async Task<ImmutableArray<Diagnostic>> Analyze(
        DiagnosticAnalyzer analyzer,
        string path,
        string content,
        IEnumerable<string>? sources = null,
        IDictionary<string, string>? properties = null
    )
    {
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = (sources ?? []).Select(s => CSharpSyntaxTree.ParseText(s, parse));

        List<MetadataReference> references = [.. Basic.Reference.Assemblies.Net80.References.All];
        references.Add(GeneratorHarness.NoesisReference);

        var compilation = CSharpCompilation.Create(
            "AnalyzerFixtures",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var broken = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (broken.Length > 0)
            throw new InvalidOperationException(
                "the analyzer fixture does not compile: "
                    + string.Join("; ", broken.Select(d => d.ToString()))
            );

        var options = new AnalyzerOptions(
            [new FixtureText(path, content)],
            new OptionsProvider(
                properties
                    ?? new Dictionary<string, string>
                    {
                        ["build_property.NoesisAnalyzeXamlBindings"] = "true",
                    }
            )
        );

        return await compilation.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync();
    }
}
