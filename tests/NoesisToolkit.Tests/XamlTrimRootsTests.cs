using Microsoft.CodeAnalysis;
using NoesisToolkit.Xaml;

namespace NoesisToolkit.Tests;

public class XamlTrimRootsTests
{
    const string Attribute = "[global::System.Diagnostics.CodeAnalysis.DynamicDependency(";

    const string Constructor =
        "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor";

    static string Member(string name, string type) => $"{Attribute}\"{name}\", typeof({type}))]";

    static string Ctor(string type) => $"{Attribute}{Constructor}, typeof({type}))]";

    static GeneratorRun Run(string fixture, params string[] sources) =>
        Run(fixture, trimmed: true, sources);

    static GeneratorRun Run(string fixture, bool trimmed, params string[] sources) =>
        GeneratorHarness.Run(
            new XamlCompileGenerator(),
            [fixture],
            [Model, Stubs.Mvvm, .. sources],
            new Dictionary<string, string>
            {
                ["build_property.ProjectDir"] = "/repo/App",
                ["build_property.EnableTrimAnalyzer"] = trimmed ? "true" : "",
            },
            extraReferences: [Other.Value]
        );

    static readonly Lazy<GeneratorRun> NativeRun = new(() =>
        Run("RootsNative.xaml", Host("NativeHost"))
    );

    static readonly Lazy<GeneratorRun> ParsedRun = new(() => Run("RootsParsed.xaml"));

    static readonly Lazy<MetadataReference> Other = new(() =>
        GeneratorHarness.Reference(
            "OtherAsm",
            """
            namespace Other
            {
                internal class InternalVm
                {
                    public string Secret { get; set; } = "";
                }
            }
            """
        )
    );

    static string Text(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(d => d.ToString()));

    static List<Diagnostic> Unrooted(GeneratorRun run) =>
        run.GeneratorDiagnostics.Where(d => d.Id == "NTK1004").ToList();

    [Test]
    public async Task A_native_binding_roots_each_hop_on_the_type_that_declares_it()
    {
        var run = NativeRun.Value;

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsNative");
        await Assert
            .That(source)
            .Contains(Member("Headline", "global::Sample.Roots.PanelViewModel"));
        await Assert.That(source).Contains(Member("Text", "global::Sample.Roots.Headline"));
        await Assert
            .That(source)
            .Contains(Member("SaveCommand", "global::Sample.Roots.PanelViewModel"));
        await Assert.That(Unrooted(run)).IsEmpty();
    }

    [Test]
    public async Task The_roots_ride_the_method_that_builds_the_document()
    {
        var source = NativeRun.Value.Source("RootsNative");

        var attribute = source.IndexOf(Attribute, StringComparison.Ordinal);
        var build = source.IndexOf("internal void BuildXamlTree()", StringComparison.Ordinal);
        await Assert.That(attribute).IsGreaterThan(-1);
        await Assert.That(build).IsGreaterThan(attribute);
        await Assert
            .That(source.Substring(attribute, build - attribute))
            .DoesNotContain("InitializeComponent");
    }

    [Test]
    public async Task An_interface_hop_roots_the_property_each_implementing_class_declares()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Member("Label", "global::Sample.Roots.Caption"));
        await Assert.That(source).DoesNotContain("typeof(global::Sample.Roots.ILabelled)");
    }

    [Test]
    public async Task A_member_only_a_derived_class_declares_is_rooted_there()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Member("Balance", "global::Sample.Roots.PointsShelf"));
    }

    [Test]
    public async Task An_element_path_roots_a_plain_property_and_leaves_a_dependency_property_alone()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Member("Caption", "global::Sample.Roots.Gauge"));
        await Assert.That(source).DoesNotContain(Member("Level", "global::Sample.Roots.Gauge"));
        await Assert.That(source).DoesNotContain("typeof(global::Noesis.");
    }

    [Test]
    public async Task An_attached_owner_in_a_path_roots_its_property_field()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Member("TextProperty", "global::Sample.Roots.Hint"));
    }

    [Test]
    public async Task An_enum_a_native_binding_ends_on_keeps_its_literals()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert
            .That(source)
            .Contains(
                $"{Attribute}global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields, typeof(global::Sample.Roots.Mood))]"
            );
    }

    [Test]
    public async Task A_binding_into_DataContext_reads_the_context_its_element_inherits()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Member("Inner", "global::Sample.Roots.PanelViewModel"));
        await Assert.That(source).Contains(Member("Note", "global::Sample.Roots.InnerViewModel"));
    }

    [Test]
    public async Task Display_member_path_roots_the_property_on_the_item_type()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Member("Name", "global::Sample.Roots.RowViewModel"));
    }

    [Test]
    public async Task Event_name_roots_the_event_on_the_host_or_the_stated_source()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Member("Pinged", "global::Sample.Roots.Gauge"));
        await Assert
            .That(source)
            .Contains(Member("Refreshed", "global::Sample.Roots.PanelViewModel"));
    }

    [Test]
    public async Task Only_an_object_constructed_inside_a_template_roots_its_constructor()
    {
        var source = NativeRun.Value.Source("RootsNative");

        await Assert.That(source).Contains(Ctor("global::Sample.Roots.Gauge"));
        await Assert.That(source).DoesNotContain(Ctor("global::Sample.Roots.Dial"));
    }

    [Test]
    public async Task A_type_generated_code_cannot_name_is_rooted_by_its_name_and_assembly()
    {
        var run = NativeRun.Value;

        await Assert.That(Text(run.Errors)).IsEmpty();
        await Assert
            .That(run.Source("RootsNative"))
            .Contains($"{Attribute}\"Secret\", \"Other.InternalVm\", \"OtherAsm\")]");
    }

    [Test]
    public async Task A_column_binding_roots_the_row_its_list_shows()
    {
        var run = ParsedRun.Value;

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsParsed");
        await Assert.That(source).Contains(Member("Detail", "global::Sample.Roots.RowViewModel"));
        await Assert.That(source).Contains(Member("Hits", "global::Sample.Roots.RowDetail"));
        await Assert.That(Unrooted(run)).IsEmpty();
    }

    [Test]
    public async Task Markup_handed_to_the_parser_roots_what_the_parser_constructs_and_sets()
    {
        var source = ParsedRun.Value.Source("RootsParsed");

        await Assert.That(source).Contains(Ctor("global::Sample.Roots.Dial"));
        await Assert.That(source).Contains(Member("Caption", "global::Sample.Roots.Dial"));
        await Assert
            .That(source)
            .Contains(
                $"{Attribute}global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields, typeof(global::Sample.Roots.Mood))]"
            );
    }

    [Test]
    public async Task A_setter_left_to_the_parser_roots_its_attached_owner_and_its_binding()
    {
        var source = ParsedRun.Value.Source("RootsParsed");

        await Assert.That(source).Contains(Member("TextProperty", "global::Sample.Roots.Hint"));
        await Assert.That(source).Contains(Member("Name", "global::Sample.Roots.RowViewModel"));
    }

    [Test]
    public async Task A_dictionary_carries_its_roots_on_its_build_method()
    {
        var source = ParsedRun.Value.Source("RootsParsed");

        var attribute = source.IndexOf(Attribute, StringComparison.Ordinal);
        var build = source.IndexOf(
            "public static global::Noesis.ResourceDictionary Build()",
            StringComparison.Ordinal
        );
        await Assert.That(attribute).IsGreaterThan(-1);
        await Assert.That(build).IsGreaterThan(attribute);
    }

    [Test]
    public async Task A_hierarchical_template_scopes_its_bindings_to_its_own_data_type()
    {
        var source = ParsedRun.Value.Source("RootsParsed");

        await Assert.That(source).Contains(Member("Children", "global::Sample.Roots.FolderNode"));
        await Assert.That(source).Contains(Member("Title", "global::Sample.Roots.FolderNode"));
    }

    [Test]
    public async Task A_document_left_to_the_loader_roots_it_whole_on_InitializeComponent()
    {
        var run = Run("RootsLoaded.xaml", LoadedHost);

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsLoaded");
        await Assert
            .That(source)
            .Contains(
                Member(
                    "ConnectEvent(System.Object,System.String,System.String)",
                    "global::Sample.Roots.LoadedHost"
                )
            );
        await Assert.That(source).Contains(Ctor("global::Sample.Roots.Dial"));
        await Assert.That(source).Contains(Member("Caption", "global::Sample.Roots.Dial"));
        await Assert.That(source).Contains(Member("Text", "global::Sample.Roots.Headline"));

        var attribute = source.IndexOf(Attribute, StringComparison.Ordinal);
        var initialize = source.IndexOf(
            "public void InitializeComponent()",
            StringComparison.Ordinal
        );
        await Assert.That(initialize).IsGreaterThan(attribute);
    }

    [Test]
    public async Task A_binding_in_a_document_left_to_the_loader_is_reported_as_native()
    {
        var run = Run("RootsLoaded.xaml", trimmed: false, LoadedHost);

        var native = run.GeneratorDiagnostics.Where(d => d.Id == "NTK1007").ToList();
        await Assert.That(native.Count).IsEqualTo(1);
        await Assert
            .That(native[0].GetMessage())
            .IsEqualTo("RootsLoaded.xaml :: binding stays native: document-left-to-the-loader");
        await Assert.That(native[0].Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(10);
    }

    static readonly Lazy<GeneratorRun> ParsedElementsRun = new(() =>
        Run("RootsParsedElements.xaml", ParsedElementsHost)
    );

    static List<int> UnrootedLines(GeneratorRun run, string name) =>
        Unrooted(run)
            .Where(d => d.GetMessage().Contains($"'{name}'", StringComparison.Ordinal))
            .Select(d => d.Location.GetLineSpan().StartLinePosition.Line + 1)
            .ToList();

    [Test]
    public async Task A_binding_on_an_element_whose_type_does_not_resolve_is_rooted()
    {
        var run = ParsedElementsRun.Value;

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsParsedElements");
        await Assert.That(source).Contains(Member("Inner", "global::Sample.Roots.PanelViewModel"));
        await Assert.That(source).Contains(Member("Note", "global::Sample.Roots.InnerViewModel"));
        await Assert
            .That(source)
            .Contains(Member("Headline", "global::Sample.Roots.PanelViewModel"));
        await Assert.That(source).Contains(Member("Text", "global::Sample.Roots.Headline"));
        await Assert.That(UnrootedLines(run, "Missing")).IsEquivalentTo([14]);
    }

    [Test]
    public async Task A_binding_held_back_from_a_parsed_element_is_reported_once_where_it_is_written()
    {
        await Assert.That(UnrootedLines(ParsedElementsRun.Value, "Lacking")).IsEquivalentTo([20]);
    }

    [Test]
    public async Task A_name_the_compiler_cannot_type_is_reported_where_it_was_written()
    {
        var run = Run("RootsUnrooted.xaml", Host("UnrootedHost"));

        await Assert.That(Text(run.Errors)).IsEmpty();
        var found = Unrooted(run)
            .Select(d =>
                (Line: d.Location.GetLineSpan().StartLinePosition.Line + 1, Text: d.GetMessage())
            )
            .OrderBy(d => d.Line)
            .ToList();

        await Assert.That(found.Count).IsEqualTo(5);
        await Assert
            .That(Unrooted(run).All(d => d.Severity == DiagnosticSeverity.Warning))
            .IsTrue();
        await Assert.That(found[0].Line).IsEqualTo(9);
        await Assert
            .That(found[0].Text)
            .IsEqualTo(
                "RootsUnrooted.xaml :: binding 'Headline.Text' reads a DataContext whose type is not declared; state it with ntk:DataType on an enclosing element"
            );
        await Assert.That(found[1].Line).IsEqualTo(12);
        await Assert
            .That(found[1].Text)
            .Contains("DisplayMemberPath 'Name' reads items whose type is not declared");
        await Assert.That(found[2].Line).IsEqualTo(13);
        await Assert.That(found[2].Text).Contains("state it with ntk:AncestorDataType");
        await Assert.That(found[3].Line).IsEqualTo(14);
        await Assert
            .That(found[3].Text)
            .Contains("reads 'Missing', which Sample.Roots.PanelViewModel does not have");
        await Assert.That(found[4].Line).IsEqualTo(15);
        await Assert.That(found[4].Text).Contains("steps through 'Loose', typed object");
    }

    [Test]
    public async Task A_project_that_does_not_trim_keeps_its_roots_and_hears_nothing_of_them()
    {
        var unrooted = Run("RootsUnrooted.xaml", trimmed: false, Host("UnrootedHost"));

        await Assert.That(Text(unrooted.Errors)).IsEmpty();
        await Assert.That(Unrooted(unrooted)).IsEmpty();

        var native = Run("RootsNative.xaml", trimmed: false, Host("NativeHost"));
        await Assert
            .That(native.Source("RootsNative"))
            .Contains(Member("Headline", "global::Sample.Roots.PanelViewModel"));
    }

    static readonly Lazy<GeneratorRun> InterfaceRun = new(() =>
        Run("RootsInterfaces.xaml", Shapes, Host("InterfaceHost"))
    );

    static readonly Lazy<GeneratorRun> PathRun = new(() =>
        Run("RootsPaths.xaml", Shapes, PathsHost)
    );

    [Test]
    public async Task A_generic_class_implementing_a_constructed_interface_roots_its_own_property()
    {
        var run = InterfaceRun.Value;

        await Assert.That(Text(run.Errors)).IsEmpty();
        await Assert
            .That(run.Source("RootsInterfaces"))
            .Contains(Member("Current", "global::Sample.Shapes.Selection<>"));
    }

    [Test]
    public async Task An_implementer_backed_by_a_dependency_property_keeps_its_wrapper_unrooted()
    {
        var source = InterfaceRun.Value.Source("RootsInterfaces");

        await Assert.That(source).Contains(Member("Label", "global::Sample.Shapes.Plaque"));
        await Assert
            .That(source)
            .DoesNotContain(Member("Label", "global::Sample.Shapes.LabelControl"));
    }

    [Test]
    public async Task An_event_an_interface_declares_is_rooted_on_the_class_implementing_it()
    {
        await Assert
            .That(InterfaceRun.Value.Source("RootsInterfaces"))
            .Contains(Member("Pinged", "global::Sample.Shapes.Pinger"));
    }

    [Test]
    public async Task An_interface_nothing_implements_is_reported_at_the_attribute_naming_it()
    {
        var found = Unrooted(InterfaceRun.Value)
            .Select(d => (Span: d.Location.GetLineSpan().Span, Text: d.GetMessage()))
            .OrderBy(d => d.Span.Start.Line)
            .ToList();

        await Assert.That(found.Count).IsEqualTo(3);
        await Assert
            .That(found[0].Text)
            .IsEqualTo(
                "RootsInterfaces.xaml :: binding 'Orphan.Label' reads 'Label' through Sample.Shapes.IOrphan, which no class the compiler can see implements"
            );
        await Assert.That(found[0].Span.Start.Line).IsEqualTo(13);
        await Assert.That(found[0].Span.Start.Character).IsEqualTo(15);
        await Assert.That(found[0].Span.End.Character).IsEqualTo(15 + "Text".Length);

        await Assert.That(found[1].Span.Start.Line).IsEqualTo(14);
        await Assert.That(found[1].Span.Start.Character).IsEqualTo(15);
        await Assert.That(found[1].Span.End.Character).IsEqualTo(15 + "r:Hint.Text".Length);

        await Assert
            .That(found[2].Text)
            .IsEqualTo(
                "RootsInterfaces.xaml :: EventName 'Silenced' names an event of Sample.Shapes.IMute, which no class the compiler can see implements"
            );
        await Assert.That(found[2].Span.Start.Line).IsEqualTo(18);
        await Assert.That(found[2].Span.Start.Character).IsEqualTo(19);
        await Assert.That(found[2].Span.End.Character).IsEqualTo(19 + "EventName".Length);
    }

    [Test]
    public async Task A_trigger_on_the_root_names_an_event_of_the_class_not_of_its_tag()
    {
        await Assert
            .That(PathRun.Value.Source("RootsPaths"))
            .Contains(Member("Opened", "global::Sample.Roots.PathsHost"));
    }

    [Test]
    public async Task A_pathless_trigger_source_is_the_element_or_ancestor_it_names()
    {
        await Assert
            .That(PathRun.Value.Source("RootsPaths"))
            .Contains(Member("Pinged", "global::Sample.Roots.Gauge"));
    }

    [Test]
    public async Task A_hop_through_a_nullable_struct_reads_the_struct()
    {
        await Assert
            .That(PathRun.Value.Source("RootsPaths"))
            .Contains(Member("X", "global::Sample.Shapes.Spot"));
    }

    [Test]
    public async Task An_indexer_a_base_class_declares_is_rooted_there()
    {
        await Assert
            .That(PathRun.Value.Source("RootsPaths"))
            .Contains(Member("Item", "global::Sample.Shapes.RowsBase"));
    }

    [Test]
    public async Task An_attached_owner_prefix_resolves_where_the_binding_is_written()
    {
        await Assert
            .That(PathRun.Value.Source("RootsPaths"))
            .Contains(Member("TextProperty", "global::Sample.Roots.Hint"));
    }

    [Test]
    public async Task Paths_the_compiler_can_follow_raise_nothing()
    {
        var run = PathRun.Value;

        await Assert.That(Text(run.Errors)).IsEmpty();
        await Assert.That(Text(Unrooted(run))).IsEmpty();
    }

    [Test]
    public async Task A_type_nested_in_or_built_from_an_open_generic_is_named_unbound()
    {
        var run = Run("RootsNames.xaml", Names, NamesFileLocal, Host("NamesHost"));

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsNames");
        await Assert
            .That(source)
            .Contains(Member("Label", "global::Sample.Names.Registry<>.Entry"));
        await Assert.That(source).Contains(Member("Title", "global::Sample.Names.Base<>"));
        await Assert.That(source).DoesNotContain("<T>");
    }

    [Test]
    public async Task A_file_local_type_is_never_named()
    {
        var run = Run("RootsNames.xaml", Names, NamesFileLocal, Host("NamesHost"));

        await Assert.That(Text(run.Errors)).IsEmpty();
        await Assert.That(run.Source("RootsNames")).DoesNotContain("Secretive");
    }

    [Test]
    public async Task A_document_left_to_the_loader_roots_properties_its_elements_and_content_set()
    {
        var run = Run("RootsLoadedContent.xaml", Shapes, ContentHost);

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsLoadedContent");
        await Assert.That(source).Contains(Member("Caption", "global::Sample.Shapes.Plate"));
        await Assert.That(source).Contains(Member("Mood", "global::Sample.Shapes.Plate"));
        await Assert.That(source).Contains(Member("Detail", "global::Sample.Shapes.Plate"));
        await Assert
            .That(source)
            .Contains(
                $"{Attribute}global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields, typeof(global::Sample.Shapes.Temper))]"
            );
        await Assert.That(source).Contains(Member("Items", "global::Sample.Shapes.Holder"));
    }

    [Test]
    public async Task An_extension_nested_in_a_parsed_binding_is_rooted()
    {
        var run = Run("RootsNestedMarkup.xaml", Shapes);

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsNestedMarkup");
        await Assert.That(source).Contains(Ctor("global::Sample.Shapes.InlineExtension"));
        await Assert
            .That(source)
            .Contains(Member("Format", "global::Sample.Shapes.InlineExtension"));
    }

    [Test]
    public async Task An_item_type_types_the_rows_and_leaves_the_column_its_own_context()
    {
        var run = Run("RootsItemTypes.xaml");

        await Assert.That(Text(run.Errors)).IsEmpty();
        await Assert.That(Text(Unrooted(run))).IsEmpty();
        var source = run.Source("RootsItemTypes");
        await Assert.That(source).Contains(Member("Detail", "global::Sample.Roots.RowViewModel"));
        await Assert.That(source).Contains(Member("Hits", "global::Sample.Roots.RowDetail"));
        await Assert.That(source).Contains(Member("Name", "global::Sample.Roots.RowViewModel"));
        await Assert
            .That(source)
            .Contains(Member("Headline", "global::Sample.Roots.PanelViewModel"));
        await Assert.That(source).Contains(Member("Text", "global::Sample.Roots.Headline"));
    }

    [Test]
    public async Task The_overrides_Noesis_probes_are_rooted_on_every_type_a_document_references()
    {
        var run = GeneratorHarness.Run(
            new XamlCompileGenerator(),
            ["RootsOverrides.xaml"],
            [Model, Stubs.Mvvm, Shapes, Host("OverridesHost")],
            new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" },
            extraReferences: [Lib.Value]
        );

        await Assert.That(Text(run.Errors)).IsEmpty();
        var source = run.Source("RootsOverrides");
        await Assert
            .That(source)
            .Contains(Member("MeasureOverride(Noesis.Size)", "global::Lib.Shelf"));
        await Assert.That(source).Contains(Member("OnApplyTemplate", "global::Lib.Shelf"));
        await Assert.That(source).Contains(Member("ToString", "global::Sample.Shapes.Option"));
        await Assert
            .That(source)
            .Contains(Member("Equals(System.Object)", "global::Sample.Shapes.Option"));
        await Assert.That(source).Contains(Member("ToString", "global::Lib.LibRow"));
        await Assert.That(source).DoesNotContain("GetHashCode");
    }

    static readonly Lazy<MetadataReference> Lib = new(() =>
        GeneratorHarness.Reference(
            "LibAsm",
            """
            namespace Lib
            {
                public class Shelf : Noesis.Control
                {
                    protected override Noesis.Size MeasureOverride(Noesis.Size availableSize) => availableSize;

                    public override void OnApplyTemplate() { }
                }

                public record LibRow(int X);
            }
            """
        )
    );

    static string Host(string name) =>
        $$"""
            namespace Sample.Roots;

            public partial class {{name}} : global::Noesis.UserControl
            {
                public {{name}}() => InitializeComponent();
            }
            """;

    const string PathsHost = """
        namespace Sample.Roots;

        public partial class PathsHost : global::Noesis.UserControl
        {
            public PathsHost() => InitializeComponent();

            public event System.EventHandler? Opened;

            public void Open() => Opened?.Invoke(this, System.EventArgs.Empty);
        }
        """;

    const string ContentHost = """
        namespace Sample.Roots;

        public partial class ContentHost : global::Noesis.UserControl
        {
            public ContentHost() => InitializeComponent();

            void OnGo(object sender, System.EventArgs e) { }
        }
        """;

    const string Shapes = """
        namespace Sample.Shapes
        {
            public interface ISelection<T>
            {
                T Current { get; }
            }

            public class Selection<T> : ISelection<T>
            {
                public T Current { get; set; } = default!;
            }

            public class Row
            {
                public string Name { get; set; } = "";
            }

            public interface ILabelled
            {
                string Label { get; }
            }

            public class Plaque : ILabelled
            {
                public string Label { get; set; } = "";
            }

            public class LabelControl : Noesis.Control, ILabelled
            {
                public static readonly Noesis.DependencyProperty LabelProperty =
                    Noesis.DependencyProperty.Register("Label", typeof(string), typeof(LabelControl), new Noesis.PropertyMetadata());

                public string Label { get => (string)(GetValue(LabelProperty) ?? ""); set => SetValue(LabelProperty, value); }
            }

            public interface IOrphan
            {
                string Label { get; }
            }

            public interface IPinger
            {
                event System.EventHandler? Pinged;
            }

            public class Pinger : IPinger
            {
                public event System.EventHandler? Pinged;

                public void Ping() => Pinged?.Invoke(this, System.EventArgs.Empty);
            }

            public interface IMute
            {
                event System.EventHandler? Silenced;
            }

            public struct Spot
            {
                public float X { get; set; }
            }

            public class RowsBase
            {
                public Row this[int index] => new Row();
            }

            public class Rows : RowsBase, System.Collections.Generic.IEnumerable<Row>
            {
                public System.Collections.Generic.IEnumerator<Row> GetEnumerator() => null!;

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
            }

            public record Option(string Label);

            public class ShapesPanel
            {
                public ISelection<Row> Selection { get; set; } = new Selection<Row>();
                public ILabelled Labelled { get; set; } = new Plaque();
                public IOrphan? Orphan { get; set; }
                public IPinger Pinger { get; set; } = new Pinger();
                public IMute? Mute { get; set; }
                public Spot? Where { get; set; }
                public Rows Rows { get; set; } = new Rows();
                public System.Collections.Generic.List<Option> Options { get; } = new();
                public Option? Picked { get; set; }
            }

            public enum Temper { Calm, Angry }

            public class Plate : Noesis.Control
            {
                public string Caption { get; set; } = "";
                public Temper Mood { get; set; }
                public Sample.Roots.RowDetail Detail { get; set; } = new Sample.Roots.RowDetail();
            }

            public class Item
            {
                public string Name { get; set; } = "";
            }

            [Noesis.ContentProperty("Items")]
            public class Holder : Noesis.Control
            {
                public System.Collections.Generic.List<Item> Items { get; } = new();
            }

            public class InlineExtension : Noesis.MarkupExtension
            {
                public string Format { get; set; } = "";

                public override object? ProvideValue(object? provider) => null;
            }
        }
        """;

    const string Names = """
        namespace Sample.Names
        {
            public interface ILabelled
            {
                string Label { get; }
            }

            public interface ITitled
            {
                string Title { get; }
            }

            public class Registry<T>
            {
                public class Entry : ILabelled
                {
                    public string Label { get; set; } = "";
                }
            }

            public class Base<T> : ITitled
            {
                public string Title { get; set; } = "";
            }

            public class Wrapped<T> : Base<System.Collections.Generic.List<T>> { }

            public class NamesPanel
            {
                public ILabelled Labelled { get; set; } = null!;
                public ITitled Titled { get; set; } = null!;
            }
        }
        """;

    const string NamesFileLocal = """
        namespace Sample.Names
        {
            file class Secretive : ILabelled
            {
                public string Label { get; set; } = "";
            }
        }
        """;

    const string LoadedHost = """
        namespace Sample.Roots;

        public partial class LoadedHost : global::Noesis.UserControl
        {
            public LoadedHost() => InitializeComponent();

            void OnGo(object sender, System.EventArgs e) { }
        }
        """;

    const string ParsedElementsHost = """
        namespace Sample.Roots;

        public partial class ParsedElementsHost : global::Noesis.UserControl
        {
            public ParsedElementsHost() => InitializeComponent();
        }

        public class Upper : global::Noesis.IValueConverter
        {
            public object? Convert(object? v, System.Type t, object? p, System.Globalization.CultureInfo c) => v;

            public object? ConvertBack(object? v, System.Type t, object? p, System.Globalization.CultureInfo c) => v;
        }
        """;

    const string Model = """
        namespace Sample.Roots
        {
            public enum Mood { Calm, Angry }

            public class Headline
            {
                public string Text { get; set; } = "";
            }

            public interface ILabelled
            {
                string Label { get; }
            }

            public class Caption : ILabelled
            {
                public string Label { get; set; } = "";
            }

            public abstract class ShelfBase { }

            public class PointsShelf : ShelfBase
            {
                public int Balance { get; set; }
            }

            public class RowDetail
            {
                public int Hits { get; set; }
            }

            public class RowViewModel
            {
                public string Name { get; set; } = "";
                public RowDetail Detail { get; set; } = new RowDetail();
            }

            public class InnerViewModel
            {
                public string Note { get; set; } = "";
            }

            public class FolderNode
            {
                public string Title { get; set; } = "";
                public System.Collections.Generic.List<FolderNode> Children { get; } = new();
            }

            public partial class PanelViewModel
            {
                public Headline Headline { get; set; } = new Headline();
                public ILabelled Labelled { get; set; } = new Caption();
                public ShelfBase Shelf { get; set; } = new PointsShelf();
                public Mood Mood { get; set; }
                public InnerViewModel Inner { get; set; } = new InnerViewModel();
                public object Loose { get; set; } = new object();
                public System.Collections.Generic.List<RowViewModel> Rows { get; } = new();

                public event System.EventHandler? Refreshed;

                [NoesisToolkit.Mvvm.DelegateCommand]
                void Save() => Refreshed?.Invoke(this, System.EventArgs.Empty);
            }

            public static class Hint
            {
                public static readonly Noesis.DependencyProperty TextProperty =
                    Noesis.DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Hint), new Noesis.PropertyMetadata());

                public static string GetText(Noesis.DependencyObject target) => "";

                public static void SetText(Noesis.DependencyObject target, string value) { }
            }

            public class Gauge : Noesis.Control
            {
                public static readonly Noesis.DependencyProperty LevelProperty =
                    Noesis.DependencyProperty.Register("Level", typeof(float), typeof(Gauge), new Noesis.PropertyMetadata());

                public float Level { get => (float)(GetValue(LevelProperty) ?? 0f); set => SetValue(LevelProperty, value); }

                public string Caption { get; set; } = "";

                public event System.EventHandler? Pinged;

                public void Ping() => Pinged?.Invoke(this, System.EventArgs.Empty);
            }

            public class Dial : Noesis.Control
            {
                public string Caption { get; set; } = "";
                public Mood Mood { get; set; }
            }

            public class Watcher : Noesis.Interactivity.TriggerBase<Noesis.FrameworkElement>
            {
                public static readonly Noesis.DependencyProperty SourceProperty =
                    Noesis.DependencyProperty.Register("Source", typeof(object), typeof(Watcher), new Noesis.PropertyMetadata());

                public object? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

                public string EventName { get; set; } = "";
            }
        }
        """;
}
