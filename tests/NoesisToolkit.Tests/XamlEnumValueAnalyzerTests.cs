using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

public class XamlEnumValueAnalyzerTests
{
    static Task<ImmutableArray<Diagnostic>> Values(string body) =>
        XamlProbe.Analyze(body, XamlBindingAnalyzer.UnresolvedEnumValueId);

    [Test]
    public async Task ResolvingMember_IsNotReported()
    {
        await Assert.That(await Values("""<ui:Badge Tone="Muted" />""")).IsEmpty();
    }

    [Test]
    public async Task RenamedMember_IsReported()
    {
        var diagnostics = await Values("""<ui:Badge Tone="Mutedd" />""");

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("Mutedd");
        await Assert.That(diagnostics[0].GetMessage()).Contains("BadgeTone");
        await Assert
            .That(diagnostics[0].Severity)
            .IsEqualTo(DiagnosticSeverity.Error)
            .Because("the value throws when the element first lays out");
    }

    [Test]
    public async Task NonEnumProperty_IsNotReported()
    {
        await Assert
            .That(await Values("""<ui:Badge Label="Anything" />"""))
            .IsEmpty()
            .Because("only an enum-typed property has a member list to check against");
    }

    [Test]
    public async Task NoesisOwnEnum_IsNotReported()
    {
        await Assert
            .That(await Values("""<Border Visibility="Sideways" />"""))
            .IsEmpty()
            .Because("Noesis converters accept aliases its own enums never declare");
    }

    [Test]
    public async Task MarkupExtensionArgument_IsCheckedAgainstItsEnumProperty()
    {
        await Assert.That(await Values("""<ui:Badge Tone="{ui:Brush Danger}" />""")).IsEmpty();
    }

    [Test]
    public async Task RenamedMarkupExtensionArgument_IsReported()
    {
        var diagnostics = await Values("""<ui:Badge Tone="{ui:Brush Dangerr}" />""");

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("Dangerr");
    }

    [Test]
    public async Task SetterValue_IsCheckedAgainstTheStyleTargetType()
    {
        var diagnostics = await Values(
            """
            <Style TargetType="ui:Badge">
              <Setter Property="Tone" Value="Nope" />
            </Style>
            """
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].GetMessage()).Contains("Nope");
    }

    [Test]
    public async Task ResolvingSetterValue_IsNotReported()
    {
        var diagnostics = await Values(
            """
            <Style TargetType="ui:Badge">
              <Setter Property="Tone" Value="Danger" />
            </Style>
            """
        );

        await Assert.That(diagnostics).IsEmpty();
    }
}
