using NoesisToolkit.Mvvm.Generators;

namespace NoesisToolkit.Tests;

public class MvvmGeneratorTests
{
    [Test]
    public async Task DependencyProperty_emits_a_registration_and_accessors()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, PropertyOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert.That(source).Contains("DependencyProperty.Register(");
        await Assert.That(source).Contains("nameof(Title)");
        await Assert.That(source).Contains("GetValue(TitleProperty)");
        await Assert.That(source).Contains("SetValue(TitleProperty, value)");
    }

    [Test]
    public async Task DelegateCommand_emits_a_fully_qualified_command_property()
    {
        var run = GeneratorHarness.Run(
            new DelegateCommandGenerator(),
            [],
            [Stubs.Mvvm, CommandOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains("global::NoesisToolkit.Mvvm.DelegateCommand SaveCommand");
    }

    [Test]
    public async Task Async_command_method_maps_to_the_async_command()
    {
        var run = GeneratorHarness.Run(
            new DelegateCommandGenerator(),
            [],
            [Stubs.Mvvm, CommandOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains("global::NoesisToolkit.Mvvm.AsyncDelegateCommand LoadCommand");
    }

    [Test]
    public async Task Non_partial_owner_is_reported_not_silently_skipped()
    {
        var run = GeneratorHarness.Run(
            new DelegateCommandGenerator(),
            [],
            [Stubs.Mvvm, SealedOwner]
        );

        await Assert.That(run.GeneratorDiagnostics.Select(d => d.Id)).Contains("NTK3101");
    }

    [Test]
    public async Task A_record_owner_is_reopened_as_a_record()
    {
        var run = GeneratorHarness.Run(
            new DelegateCommandGenerator(),
            [],
            [Stubs.Mvvm, RecordCommandOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert.That(source).Contains("partial record Note");
        await Assert.That(source).Contains("partial record struct Tag");
    }

    [Test]
    public async Task Nested_owner_emits_one_partial_block_per_level()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, NestedPropertyOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).DoesNotContain("class Outer.class");
        await Assert.That(run.AllSources).Contains("partial class Outer");
        await Assert.That(run.AllSources).Contains("partial class Inner");
    }

    [Test]
    public async Task Internal_command_owner_keeps_its_accessibility()
    {
        var run = GeneratorHarness.Run(
            new DelegateCommandGenerator(),
            [],
            [Stubs.Mvvm, InternalCommandOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).Contains("partial class Hidden");
        await Assert.That(run.AllSources).Contains("SaveCommand");
        await Assert.That(run.AllSources).DoesNotContain("public partial class Hidden");
    }

    [Test]
    public async Task Nested_command_owner_emits_one_partial_block_per_level()
    {
        var run = GeneratorHarness.Run(
            new DelegateCommandGenerator(),
            [],
            [Stubs.Mvvm, NestedCommandOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.AllSources).Contains("partial class Shell");
        await Assert.That(run.AllSources).Contains("partial class Panel");
    }

    [Test]
    public async Task A_static_partial_property_registers_as_attached()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, AttachedOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert.That(source).Contains("DependencyProperty.RegisterAttached(");
        await Assert.That(source).Contains("GetSlot(");
        await Assert.That(source).Contains("SetSlot(");
    }

    [Test]
    public async Task Metadata_arguments_reach_the_registration()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, MetadataOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert.That(source).Contains("OnTitleChanged");
        await Assert.That(source).Contains("AffectsMeasure");
    }

    [Test]
    public async Task Named_arguments_land_in_the_right_slot()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, NamedArgOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        // Read positionally, AffectsRender would land in the propertyChanged slot.
        var source = run.AllSources;
        await Assert.That(source).Contains("OnTitleChanged");
        await Assert.That(source).Contains("AffectsRender");
    }

    [Test]
    public async Task A_non_partial_property_owner_is_reported()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, NonPartialPropertyOwner]
        );

        await Assert.That(run.GeneratorDiagnostics.Select(d => d.Id)).Contains("NTK3001");
    }

    const string AttachedOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Grid : global::Noesis.UserControl
        {
            [DependencyProperty]
            public static partial int Slot { get; set; }
        }
        """;

    const string MetadataOwner = """
        using NoesisToolkit.Mvvm;
        using Noesis;

        namespace Sample;

        public partial class Metered : global::Noesis.UserControl
        {
            [DependencyProperty(0f, nameof(OnTitleChanged), FrameworkPropertyMetadataOptions.AffectsMeasure)]
            public partial float Size { get; set; }

            static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }
        }
        """;

    const string NamedArgOwner = """
        using NoesisToolkit.Mvvm;
        using Noesis;

        namespace Sample;

        public partial class Named : global::Noesis.UserControl
        {
            [DependencyProperty(null, metadataOptions: Noesis.FrameworkPropertyMetadataOptions.AffectsRender, propertyChanged: nameof(OnTitleChanged))]
            public partial string? Title { get; set; }

            static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }
        }
        """;

    const string NonPartialPropertyOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public class Sealed : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial string? Title { get; set; }
        }
        """;

    const string NestedPropertyOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Outer
        {
            public partial class Inner : global::Noesis.UserControl
            {
                [DependencyProperty]
                public partial string? Title { get; set; }
            }
        }
        """;

    const string InternalCommandOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        internal partial class Hidden
        {
            [DelegateCommand]
            void Save() { }
        }
        """;

    const string RecordCommandOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial record Note
        {
            [DelegateCommand]
            void Save() { }
        }

        public partial record struct Tag
        {
            [DelegateCommand]
            void Pin() { }
        }
        """;

    const string NestedCommandOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Shell
        {
            public partial class Panel
            {
                [DelegateCommand]
                void Save() { }
            }
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

    const string CommandOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Editor
        {
            [DelegateCommand]
            void Save() { }

            [DelegateCommand]
            async System.Threading.Tasks.ValueTask Load() => await System.Threading.Tasks.Task.CompletedTask;
        }
        """;

    const string SealedOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public class NotPartial
        {
            [DelegateCommand]
            void Save() { }
        }
        """;
}
