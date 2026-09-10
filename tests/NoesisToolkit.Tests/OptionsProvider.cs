using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace NoesisToolkit.Tests;

sealed class OptionsProvider(IDictionary<string, string>? values) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } =
        new Bag(values ?? new Dictionary<string, string>());

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

    sealed class Bag(IDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var hit))
            {
                value = hit;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}

sealed class FixtureText(string path, string text) : AdditionalText
{
    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) =>
        SourceText.From(text, Encoding.UTF8);
}
