using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

/// <summary>Runs the binding analyzer over one dictionary body, which is how every one of its
/// test files asks its question.</summary>
static class XamlProbe
{
    /// <summary>The zero-based line <c>extraXmlns</c> lands on, so a test about where a diagnostic
    /// points does not hardcode this file's shape.</summary>
    public const int ExtraXmlnsLine = 6;

    public static async Task<ImmutableArray<Diagnostic>> Analyze(
        string body,
        string id,
        string extraXmlns = ""
    )
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new XamlBindingAnalyzer(),
            "/tmp/Probe.xaml",
            $"""
            <ResourceDictionary
              xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:ui="clr-namespace:Sample.Ui"
              xmlns:social="clr-namespace:Sample.Social"
              xmlns:ntk="https://github.com/vrimar/NoesisToolkit"
              {extraXmlns}>
              {body}
            </ResourceDictionary>
            """,
            Stubs.Sample
        );

        return diagnostics.Where(d => d.Id == id).ToImmutableArray();
    }
}
