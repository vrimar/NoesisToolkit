using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

public class XamlStaticAnalyzerTests
{
    static Task<ImmutableArray<Diagnostic>> Statics(string body) =>
        XamlProbe.Analyze(
            body,
            XamlBindingAnalyzer.UnresolvedStaticId,
            @"xmlns:dead=""clr-namespace:Sample.Enums"""
        );

    [Test]
    public async Task ResolvingMember_IsNotReported()
    {
        var diagnostics = await Statics(
            """<Border Tag="{x:Static social:GuildMemberListSortBy.Name}" />"""
        );

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task RenamedMember_IsReported()
    {
        var diagnostics = await Statics(
            """<Border Tag="{x:Static social:GuildMemberListSortBy.Contribution}" />"""
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert
            .That(diagnostics[0].GetMessage())
            .Contains("Sample.Social.GuildMemberListSortBy.Contribution");
        await Assert
            .That(diagnostics[0].Severity)
            .IsEqualTo(DiagnosticSeverity.Error)
            .Because("the member silently resolves to null, so a warning would be missed");
    }

    [Test]
    public async Task RenamedType_IsReported()
    {
        var diagnostics = await Statics(
            """<Border Tag="{x:Static social:GuildMemberSortBy.Name}" />"""
        );

        await Assert.That(diagnostics.Length).IsEqualTo(1);
    }

    [Test]
    public async Task DeadNamespace_IsLeftToTheNamespaceRule()
    {
        var body = """<Border Tag="{x:Static dead:SortType.Ascending}" />""";

        await Assert
            .That(await Statics(body))
            .IsEmpty()
            .Because("one root cause should not produce two diagnostics");
        await Assert
            .That(
                await XamlProbe.Analyze(
                    body,
                    XamlBindingAnalyzer.UnresolvedNamespaceId,
                    @"xmlns:dead=""clr-namespace:Sample.Enums"""
                )
            )
            .IsNotEmpty();
    }

    [Test]
    public async Task UnprefixedStatic_ResolvesAgainstThePresentationNamespace()
    {
        var diagnostics = await Statics("""<Border Tag="{x:Static Visibility.Collapsed}" />""");

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task UnprefixedStatic_IsStillCheckedWhenItMisses()
    {
        var diagnostics = await Statics("""<Border Tag="{x:Static Visibility.Sideways}" />""");

        await Assert.That(diagnostics.Length).IsEqualTo(1);
    }
}
