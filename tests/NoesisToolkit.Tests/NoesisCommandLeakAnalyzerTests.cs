using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NoesisToolkit.Analyzers;

namespace NoesisToolkit.Tests;

public class NoesisCommandLeakAnalyzerTests
{
    const string Preamble = """
        namespace NoesisToolkit.Mvvm;

        public sealed class DelegateCommandAttribute : System.Attribute { }

        public sealed class DelegateCommand : System.Windows.Input.ICommand
        {
            public DelegateCommand(System.Action execute) { }

            public DelegateCommand(System.Action execute, System.Func<bool> canExecute) { }

            public event System.EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter) { }
        }
        """;

    static async Task<ImmutableArray<Diagnostic>> Leaks(string source)
    {
        var diagnostics = await AnalyzerHarness.Analyze(
            new NoesisCommandLeakAnalyzer(),
            "/tmp/unused.xaml",
            "<x />",
            [Preamble, source]
        );

        return diagnostics
            .Where(d => d.Id == NoesisCommandLeakAnalyzer.SelfCapturingCommandId)
            .ToImmutableArray();
    }

    static string Control(string body) =>
        $$"""
            using NoesisToolkit.Mvvm;

            namespace Sample;

            public partial class Pager : global::Noesis.Control
            {
                int _page;

                {{body}}

                void Turn() { }

                static void TurnStatic() { }
            }
            """;

    [Test]
    [Arguments(
        "public DelegateCommand Next { get; } public Pager() { Next = new DelegateCommand(Turn); }",
        1
    )]
    [Arguments(
        "public DelegateCommand Next { get; } public Pager() { Next = new DelegateCommand(() => Turn()); }",
        1
    )]
    [Arguments("DelegateCommand? _next; public Pager() { _next = new DelegateCommand(Turn); }", 1)]
    [Arguments(
        "DelegateCommand? _next; public DelegateCommand Next => _next ??= new DelegateCommand(Turn);",
        1
    )]
    [Arguments(
        "public DelegateCommand Next { get; } public Pager() { Next = new DelegateCommand(() => Turn(), () => _page > 0); }",
        1
    )]
    [Arguments(
        "public DelegateCommand Next { get; } public Pager() { Next = new DelegateCommand(TurnStatic); }",
        0
    )]
    [Arguments(
        "public DelegateCommand Next { get; } public Pager() { Next = new DelegateCommand(static () => { }); }",
        0
    )]
    // A command handed straight to a collaborator is that collaborator's lifetime, not the control's.
    [Arguments(
        "public Pager() { Use(new DelegateCommand(Turn)); } static void Use(DelegateCommand c) { }",
        0
    )]
    public async Task A_command_is_reported_only_when_the_control_keeps_one_that_captures_it(
        string body,
        int expected
    )
    {
        var leaks = await Leaks(Control(body));

        await Assert.That(leaks.Length).IsEqualTo(expected);
    }

    [Test]
    public async Task A_generated_command_on_a_control_is_reported_at_its_attribute()
    {
        var leaks = await Leaks(Control("[DelegateCommand] void Advance() { }"));

        await Assert.That(leaks.Length).IsEqualTo(1);
        await Assert.That(leaks[0].GetMessage()).Contains("AdvanceCommand");
    }

    [Test]
    public async Task A_command_on_a_plain_class_is_not_a_leak()
    {
        var leaks = await Leaks(
            """
            using NoesisToolkit.Mvvm;

            namespace Sample;

            public partial class PagerViewModel
            {
                public DelegateCommand Next { get; }

                public PagerViewModel() { Next = new DelegateCommand(Turn); }

                [DelegateCommand]
                void Advance() { }

                void Turn() { }
            }
            """
        );

        await Assert.That(leaks).IsEmpty();
    }
}
