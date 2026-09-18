using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

public class NoesisEventLeakAnalyzerTests
{
    static async Task<ImmutableArray<Diagnostic>> Leaks(string body)
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new NoesisEventLeakAnalyzer(),
            "/tmp/unused.xaml",
            "<x />",
            [
                $$"""
                namespace Sample;

                public partial class Panel : global::Noesis.UserControl
                {
                    int _count;
                    readonly global::Noesis.Button _button = new global::Noesis.Button();

                    {{body}}

                    void OnClick(object sender, System.EventArgs e) { }

                    void OnOther(object sender, System.EventArgs e) { }

                    static void OnStatic(object sender, System.EventArgs e) { }
                }
                """,
            ]
        );

        return diagnostics
            .Where(d => d.Id == NoesisEventLeakAnalyzer.UnbalancedNoesisEventId)
            .ToImmutableArray();
    }

    [Test]
    [Arguments("void Hook() { _button.Click += OnClick; }", 1)]
    [Arguments(
        "void Hook() { _button.Click += OnClick; } void Drop() { _button.Click -= OnClick; }",
        0
    )]
    [Arguments("void Hook() { _button.Click += OnStatic; }", 0)]
    [Arguments("void Hook() { _button.Click += (s, e) => _count++; }", 1)]
    [Arguments("void Hook() { _button.Click += (s, e) => { }; }", 0)]
    [Arguments(
        "void Hook() { _button.Click += OnClick; } void Drop() { _button.Click -= (s, e) => _count++; }",
        1
    )]
    [Arguments(
        "void Hook() { _button.Click += OnClick; } void Drop() { _button.Click -= OnOther; }",
        1
    )]
    public async Task A_subscription_is_reported_only_when_it_pins_the_element(
        string body,
        int expected
    )
    {
        var leaks = await Leaks(body);

        await Assert.That(leaks.Length).IsEqualTo(expected);
    }

    [Test]
    [Arguments("void Hook() { _button.Click -= OnClick; _button.Click += OnClick; }", 1)]
    [Arguments(
        "void Hook() { if (_count == 0) { _button.Click -= OnClick; } if (_count == 1) { _button.Click += OnClick; } }",
        1
    )]
    [Arguments(
        "void Hook() { _button.Click -= OnClick; _button.Click += OnClick; } void Drop() { _button.Click -= OnClick; }",
        0
    )]
    // Branches of one `if` are alternatives: the '-=' is the teardown half of a toggle.
    [Arguments(
        "void Hook(bool on) { if (on) { _button.Click += OnClick; } else { _button.Click -= OnClick; } }",
        0
    )]
    [Arguments("void Hook() { _button.Click += OnClick; _button.Click -= OnClick; }", 0)]
    // A handler behind a parameter may be static or not, so its guard still has to be called out.
    [Arguments("void Hook(System.EventHandler h) { _button.Click -= h; _button.Click += h; }", 1)]
    public async Task An_unsubscribe_the_same_flow_re_attaches_is_not_a_teardown(
        string body,
        int expected
    )
    {
        var leaks = await Leaks(body);

        await Assert.That(leaks.Length).IsEqualTo(expected);
    }

    [Test]
    public async Task A_teardown_in_another_file_of_a_partial_type_still_balances()
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new NoesisEventLeakAnalyzer(),
            "/tmp/unused.xaml",
            "<x />",
            [
                """
                namespace Sample;

                public partial class Split : global::Noesis.UserControl
                {
                    readonly global::Noesis.Button _button = new global::Noesis.Button();

                    void Hook() { _button.Click -= OnClick; _button.Click += OnClick; }

                    void OnClick(object sender, System.EventArgs e) { }
                }
                """,
                """
                namespace Sample;

                public partial class Split
                {
                    void Drop() { _button.Click -= OnClick; }
                }
                """,
            ]
        );

        await Assert
            .That(diagnostics.Where(d => d.Id == NoesisEventLeakAnalyzer.UnbalancedNoesisEventId))
            .IsEmpty();
    }

    [Test]
    public async Task A_plain_managed_event_is_not_a_Noesis_leak()
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new NoesisEventLeakAnalyzer(),
            "/tmp/unused.xaml",
            "<x />",
            [
                """
                namespace Sample;

                public class Source { public event System.EventHandler? Fired; }

                public partial class Host : global::Noesis.UserControl
                {
                    readonly Source _source = new Source();

                    void Hook() { _source.Fired += OnFired; }

                    void OnFired(object? sender, System.EventArgs e) { }
                }
                """,
            ]
        );

        await Assert
            .That(diagnostics.Where(d => d.Id == NoesisEventLeakAnalyzer.UnbalancedNoesisEventId))
            .IsEmpty();
    }
}
