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
        await Assert.That(source).Contains("DependencyRead.String(this, TitleProperty)");
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
        await Assert
            .That(source)
            .Contains(
                "global::NoesisToolkit.Mvvm.CodeGen.DependencyRead.Int(element, SlotProperty)"
            );
    }

    [Test]
    public async Task A_long_property_reads_and_writes_through_the_typed_natives()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, LongOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        var source = run.AllSources;
        await Assert
            .That(source)
            .Contains("global::NoesisToolkit.Mvvm.CodeGen.DependencyRead.Long(this, MaxProperty)");
        await Assert
            .That(source)
            .Contains(
                "global::NoesisToolkit.Mvvm.CodeGen.DependencyWrite.Value(this, MaxProperty, value)"
            );
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
    public async Task A_callback_that_takes_a_handle_registers_through_the_handle_metadata()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, HandleCallbackOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains(
                "global::NoesisToolkit.Mvvm.CodeGen.DependencyWatcher.Metadata(default(object), FrameworkPropertyMetadataOptions.None, OnHintChanged)"
            );
    }

    [Test]
    public async Task An_attached_property_reads_by_handle_as_it_reads_by_object()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, AttachedOwner, HandleCallbackOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains(
                "public static int GetSlot(global::NoesisToolkit.Mvvm.ElementHandle element) =>"
            );
        await Assert
            .That(run.AllSources)
            .Contains(
                "global::NoesisToolkit.Mvvm.CodeGen.DependencyRead.Int(element, SlotProperty)"
            );
        await Assert
            .That(run.AllSources)
            .Contains(
                "global::NoesisToolkit.Mvvm.CodeGen.DependencyRead.Value(element, HintProperty)!"
            );
    }

    [Test]
    public async Task A_property_without_a_callback_registers_through_the_bare_metadata()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, AttachedOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains(
                "global::NoesisToolkit.Mvvm.CodeGen.DependencyWatcher.Metadata(default(int), FrameworkPropertyMetadataOptions.None)"
            );
    }

    [Test]
    public async Task A_callback_written_as_null_is_no_callback()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, NullCallbackOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert
            .That(run.AllSources)
            .Contains(
                "global::NoesisToolkit.Mvvm.CodeGen.DependencyWatcher.Metadata(default(object), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault)"
            );
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

    [Test]
    public async Task A_settable_GridLength_property_is_reported_and_a_get_only_one_is_not()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, GridLengthOwner]
        );

        var misrouted = run.GeneratorDiagnostics.Where(d => d.Id == "NTK3002").ToList();
        await Assert.That(misrouted.Count).IsEqualTo(1);
        await Assert.That(misrouted[0].GetMessage()).Contains("'Settable'");
    }

    [Test]
    public async Task A_Matrix_property_is_refused_whether_or_not_it_is_settable()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, MatrixOwner]
        );

        var ids = run.GeneratorDiagnostics.Select(d => d.Id).ToList();
        await Assert.That(ids.Count(id => id == "NTK3003")).IsEqualTo(2);
        await Assert.That(ids).DoesNotContain("NTK3002");
    }

    [Test]
    public async Task A_property_without_an_access_modifier_is_implemented_without_one()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, ImplicitlyPrivateOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
    }

    [Test]
    public async Task A_property_is_implemented_with_its_declared_inheritance_modifiers()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, InheritanceModifiersOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
    }

    [Test]
    public async Task A_property_redeclared_over_a_generated_one_hides_its_field()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, RedeclaringOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.CompileDiagnostics.Select(d => d.Id)).DoesNotContain("CS0108");
    }

    [Test]
    public async Task A_redeclared_attached_property_hides_exactly_the_accessors_it_shadows()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, RedeclaringAttachedOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        var ids = run.CompileDiagnostics.Select(d => d.Id).ToList();
        await Assert.That(ids).DoesNotContain("CS0108");
        await Assert.That(ids).DoesNotContain("CS0109");
    }

    [Test]
    public async Task A_property_with_its_own_implementing_declaration_is_reported_unless_attached()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, ImplementedPropertyOwner]
        );

        await Assert.That(run.Errors.Select(d => d.Id)).IsEquivalentTo(["NTK3005", "NTK3005"]);
    }

    [Test]
    public async Task Owners_whose_names_flatten_alike_all_generate()
    {
        var run = GeneratorHarness.Run(
            [new DependencyPropertyGenerator(), new DelegateCommandGenerator()],
            [],
            [Stubs.Mvvm, LookalikeOwners]
        );

        await Assert.That(run.Errors).IsEmpty();
    }

    [Test]
    public async Task A_non_partial_instance_property_is_reported_and_not_generated()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, NonPartialPropertyMember]
        );

        await Assert.That(run.Errors.Select(d => d.Id)).IsEquivalentTo(["NTK3004"]);
    }

    [Test]
    public async Task Same_named_types_nested_in_a_generic_and_a_plain_outer_both_generate()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, GenericOuterOwners]
        );

        await Assert.That(run.Errors).IsEmpty();
    }

    [Test]
    public async Task A_command_is_no_more_accessible_than_its_parameter_type()
    {
        var run = GeneratorHarness.Run(
            new DelegateCommandGenerator(),
            [],
            [Stubs.Mvvm, InternalParameterOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
    }

    [Test]
    public async Task Keyword_names_are_escaped_in_the_generated_code()
    {
        var run = GeneratorHarness.Run(
            [new DependencyPropertyGenerator(), new DelegateCommandGenerator()],
            [],
            [Stubs.Mvvm, KeywordOwner]
        );

        await Assert.That(run.Errors).IsEmpty();
        await Assert.That(run.CompileDiagnostics.Select(d => d.Id)).DoesNotContain("CS9258");
    }

    [Test]
    public async Task A_non_partial_outer_type_is_reported_where_it_is_declared()
    {
        var run = GeneratorHarness.Run(
            new DependencyPropertyGenerator(),
            [],
            [Stubs.Mvvm, NonPartialOuterOwner]
        );

        var span = run.GeneratorDiagnostics.Single(d => d.Id == "NTK3001").Location.SourceSpan;
        await Assert
            .That(NonPartialOuterOwner.Substring(span.Start, span.Length))
            .IsEqualTo("Frame");
    }

    const string ImplicitlyPrivateOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Tally : global::Noesis.UserControl
        {
            [DependencyProperty]
            partial int Count { get; set; }

            [DependencyProperty]
            static partial int Slot { get; set; }
        }
        """;

    const string InheritanceModifiersOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public class Plain : global::Noesis.UserControl
        {
            public string Title => "";
        }

        public partial class Card : Plain
        {
            [DependencyProperty]
            public partial string? Title { get; set; }

            [DependencyProperty]
            public virtual partial int Count { get; set; }
        }
        """;

    const string RedeclaringOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Gauge : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial int Count { get; set; }
        }

        public partial class LabeledGauge : Gauge
        {
            [DependencyProperty]
            public new partial int Count { get; set; }
        }
        """;

    const string RedeclaringAttachedOwner = """
        using NoesisToolkit.Mvvm;
        using Noesis;

        namespace Sample;

        public partial class Dock : global::Noesis.UserControl
        {
            [DependencyProperty]
            public static partial int Slot { get; set; }
        }

        public partial class LabeledDock : Dock
        {
            [DependencyProperty]
            public static new partial int Slot { get; set; }
        }

        public class Classic : global::Noesis.UserControl
        {
            public static readonly DependencyProperty RowProperty = null!;

            public static int GetRow(DependencyObject element) => 0;

            public static void SetRow(DependencyObject element, int value) { }
        }

        public partial class Modern : Classic
        {
            [DependencyProperty]
            public static partial int Row { get; set; }
        }
        """;

    const string ImplementedPropertyOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Meter : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial int Count { get; set; }

            public partial int Count { get => 0; set { } }

            public partial int Total { get; set; }

            [DependencyProperty]
            public partial int Total { get => 0; set { } }

            [DependencyProperty]
            public static partial int Slot { get; set; }

            public static partial int Slot { get => 0; set { } }
        }
        """;

    const string LookalikeOwners = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Foo_Inner : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial int A { get; set; }
        }

        public partial class Foo
        {
            public partial class Inner : global::Noesis.UserControl
            {
                [DependencyProperty]
                public partial int B { get; set; }
            }
        }

        public partial class Case : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial int C { get; set; }

            [DelegateCommand]
            void Save() { }
        }

        public partial class CASE : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial int D { get; set; }

            [DelegateCommand]
            void Load() { }
        }
        """;

    const string NonPartialPropertyMember = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Counter : global::Noesis.UserControl
        {
            [DependencyProperty]
            public int Count { get; set; }

            [DependencyProperty]
            public partial int Total { get; set; }
        }
        """;

    const string GenericOuterOwners = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Host
        {
            public partial class Inner : global::Noesis.UserControl
            {
                [DependencyProperty]
                public partial int A { get; set; }
            }
        }

        public partial class Host<T>
        {
            public partial class Inner : global::Noesis.UserControl
            {
                [DependencyProperty]
                public partial int B { get; set; }
            }
        }
        """;

    const string InternalParameterOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        internal sealed class Item { }

        public partial class Picker
        {
            [DelegateCommand]
            void Select(Item item) { }

            [DelegateCommand]
            void SelectAll(System.Collections.Generic.List<Item> items) { }
        }
        """;

    const string KeywordOwner = """
        using NoesisToolkit.Mvvm;
        using Noesis;

        namespace Sample;

        public enum Mode { @default, @fixed }

        public partial class @event : global::Noesis.UserControl
        {
            [DependencyProperty(Mode.@fixed, nameof(@checked))]
            public partial Mode @default { get; set; }

            [DelegateCommand]
            void @lock() { }

            [DelegateCommand]
            void field() { }

            static void @checked(DependencyObject d, DependencyPropertyChangedEventArgs e) { }
        }
        """;

    const string NonPartialOuterOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public class Frame
        {
            public partial class Pane : global::Noesis.UserControl
            {
                [DependencyProperty]
                public partial string? Title { get; set; }
            }
        }
        """;

    const string MatrixOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Plate : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial global::Noesis.Matrix Fixed { get; }

            [DependencyProperty]
            public partial global::Noesis.Matrix Settable { get; set; }
        }
        """;

    const string GridLengthOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Gutter : global::Noesis.UserControl
        {
            [DependencyProperty]
            public partial global::Noesis.GridLength Settable { get; set; }

            [DependencyProperty]
            public partial global::Noesis.GridLength Fixed { get; }
        }
        """;

    const string AttachedOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Grid : global::Noesis.UserControl
        {
            [DependencyProperty]
            public static partial int Slot { get; set; }
        }
        """;

    const string LongOwner = """
        using NoesisToolkit.Mvvm;

        namespace Sample;

        public partial class Bounded : global::Noesis.UserControl
        {
            [DependencyProperty(long.MaxValue)]
            public partial long Max { get; set; }
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

    const string HandleCallbackOwner = """
        using NoesisToolkit.Mvvm;
        using Noesis;

        namespace Sample;

        public static partial class Hints
        {
            [DependencyProperty(null, nameof(OnHintChanged))]
            public static partial object Hint { get; set; }

            static void OnHintChanged(ElementHandle element, DependencyPropertyChangedEventArgs e) { }
        }
        """;

    const string NullCallbackOwner = """
        using NoesisToolkit.Mvvm;
        using Noesis;

        namespace Sample;

        public static partial class Sorts
        {
            [DependencyProperty(null, null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault)]
            public static partial object Key { get; set; }
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
