using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

public class XamlElementTypeAnalyzerTests
{
    static Task<ImmutableArray<Diagnostic>> ElementTypes(string body) =>
        XamlProbe.Analyze(body, XamlBindingAnalyzer.UnresolvedElementTypeId);

    [Test]
    public async Task AnElementNamingNoType_IsReported()
    {
        var diagnostics = await ElementTypes("""<ui:SpellBar />""");

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("Sample.Ui.SpellBar");
    }

    [Test]
    public async Task ATypeItsPropertyElementAndAnExtensionWithoutItsSuffix_AreNotReported()
    {
        var diagnostics = await ElementTypes(
            """
            <ui:Badge>
              <ui:Badge.Tag>
                <ui:Brush />
              </ui:Badge.Tag>
            </ui:Badge>
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task AnElementInAnIgnorableNamespaceThatResolves_IsNotReported()
    {
        var diagnostics = await XamlProbe.Analyze(
            """<design:SpellBar><ui:SpellBar /></design:SpellBar>""",
            XamlBindingAnalyzer.UnresolvedElementTypeId,
            """
            xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
            xmlns:design="clr-namespace:Sample.Social"
            mc:Ignorable="design"
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task ATypeTwoReferencedAssembliesDeclare_IsNotReported()
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new XamlBindingAnalyzer(),
            "/tmp/Probe.xaml",
            """
            <ResourceDictionary
              xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:copies="clr-namespace:Sample.Copies">
              <copies:Badge x:Key="Badge" />
            </ResourceDictionary>
            """,
            libraries: [Library("CopyA", "public"), Library("CopyB", "internal")]
        );

        await Assert
            .That(diagnostics.Where(d => d.Id == XamlBindingAnalyzer.UnresolvedElementTypeId))
            .IsEmpty();
    }

    static MetadataReference Library(string name, string accessibility) =>
        CSharpCompilation
            .Create(
                name,
                [
                    CSharpSyntaxTree.ParseText(
                        $"namespace Sample.Copies {{ {accessibility} class Badge {{ }} }}"
                    ),
                ],
                GeneratorHarness.Framework,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            )
            .ToMetadataReference();
}
