using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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
}
