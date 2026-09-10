using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

public class XamlNamespaceAnalyzerTests
{
    static Task<ImmutableArray<Diagnostic>> Analyze(string xmlns) =>
        XamlProbe.Analyze("", XamlBindingAnalyzer.UnresolvedNamespaceId, xmlns);

    [Test]
    public async Task ResolvingNamespace_IsNotReported()
    {
        var diagnostics = await Analyze("""xmlns:more="clr-namespace:Sample.Social" """);

        await Assert
            .That(diagnostics)
            .IsEmpty()
            .Because("Sample.Social exists, and the assembly= suffix is not part of the name");
    }

    [Test]
    public async Task DeadNamespace_IsReportedAsAnError()
    {
        var diagnostics = await Analyze("""xmlns:enums="clr-namespace:Sample.Enums" """);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert
            .That(diagnostics[0].Severity)
            .IsEqualTo(DiagnosticSeverity.Error)
            .Because("the namespace silently fails to load, so a warning would be missed");
        await Assert.That(diagnostics[0].GetMessage()).Contains("Sample.Enums");
    }

    [Test]
    public async Task DeadNamespace_ReportsAtItsOwnDeclaration()
    {
        var diagnostics = await Analyze("""xmlns:enums="clr-namespace:Sample.Enums" """);

        var line = diagnostics[0].Location.GetLineSpan().StartLinePosition.Line;

        await Assert.That(line).IsEqualTo(XamlProbe.ExtraXmlnsLine);
    }

    [Test]
    public async Task DeclaredButUnusedNamespace_IsJudgedOnResolutionAlone()
    {
        var diagnostics = await Analyze("""xmlns:items="clr-namespace:Sample.Social" """);

        await Assert
            .That(diagnostics)
            .IsEmpty()
            .Because(
                "a resolving namespace is fine whether or not the file reaches a type through it"
            );
    }

    [Test]
    public async Task PresentationNamespaces_AreLeftAlone()
    {
        var diagnostics = await Analyze(
            """xmlns:b="http://schemas.microsoft.com/xaml/behaviors" """
        );

        await Assert
            .That(diagnostics)
            .IsEmpty()
            .Because("only clr-namespace declarations name a CLR namespace to resolve");
    }
}
