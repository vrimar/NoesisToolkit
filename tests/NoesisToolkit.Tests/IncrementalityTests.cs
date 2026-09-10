using Microsoft.CodeAnalysis;
using NoesisToolkit.Mvvm.Generators;
using NoesisToolkit.Xaml;

namespace NoesisToolkit.Tests;

public class IncrementalityTests
{
    static async Task ShouldAllBeCached(IReadOnlyList<IncrementalStepRunReason> reasons)
    {
        await Assert.That(reasons).IsNotEmpty();
        await Assert
            .That(
                reasons.Where(r =>
                    r is not (IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task DelegateCommand_survives_an_unrelated_edit()
    {
        await ShouldAllBeCached(
            GeneratorHarness.RunAfterUnrelatedEdit(
                new DelegateCommandGenerator(),
                [],
                [Stubs.Mvvm, CommandOwner],
                null,
                [DelegateCommandGenerator.CommandsStep]
            )
        );
    }

    [Test]
    public async Task DependencyProperty_survives_an_unrelated_edit()
    {
        await ShouldAllBeCached(
            GeneratorHarness.RunAfterUnrelatedEdit(
                new DependencyPropertyGenerator(),
                [],
                [Stubs.Mvvm, PropertyOwner],
                null,
                [DependencyPropertyGenerator.PropertiesStep]
            )
        );
    }

    [Test]
    public async Task Xaml_compiler_reads_each_document_once_per_edit()
    {
        var reasons = GeneratorHarness.RunAfterUnrelatedEdit(
            new XamlCompileGenerator(),
            ["Theme.xaml"],
            null,
            new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" },
            [XamlCompileGenerator.DocumentsStep, XamlCompileGenerator.OptionsStep]
        );

        await ShouldAllBeCached(reasons);
    }

    const string CommandOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Editor
        {
            [DelegateCommand]
            void Save() { }
        }
        """;

    const string PropertyOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Shell : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial string? Title { get; set; }
        }
        """;
}
