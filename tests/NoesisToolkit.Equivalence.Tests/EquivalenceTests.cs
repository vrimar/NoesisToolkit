using Noesis;
using NoesisToolkit.Testing;
using Path = System.IO.Path;

namespace NoesisToolkit.Equivalence.Tests;

// Proves shape, not timing: both sides are compared with the graph already flushed, so a lookup
// performed too early still resolves here.
[NotInParallel("Noesis")]
public sealed class EquivalenceTests
{
    static void Live() => NoesisRuntime.Start();

    [Test]
    public async Task Every_compiled_dictionary_matches_the_parsed_graph()
    {
        Live();

        var failures = XamlParity.Check(
            XamlGenerated.NoesisToolkitEquivalenceTests.XamlRegistry.Dictionaries,
            XamlGenerated.NoesisToolkitEquivalenceTests.XamlResources.Flush,
            Dumps
        );

        await Assert.That(string.Join("\n", failures)).IsEmpty();
    }

    [Test]
    public async Task The_compiled_root_matches_the_parsed_one()
    {
        Live();

        var compiled = new RootFixture();
        compiled.InitializeComponent();

        // Its constructor builds nothing, so the parser gets to fill this instance itself.
        var parsed = (RootFixture)GUI.LoadXaml("/Fixtures;Fixtures/Root.xaml");

        // A root's x:Name becomes a code-behind field; a template's stays in the template scope.
        await Assert.That(compiled.Frame).IsNotNull();
        await Assert.That(compiled.Stack).IsNotNull();
        await Assert.That(compiled.Action).IsNotNull();

        await Assert
            .That(XamlParity.Compare("Root.xaml", parsed, compiled, Dumps)?.ToString())
            .IsNull();
    }

    [Test]
    public async Task Every_fixture_on_disk_is_compiled()
    {
        var dictionaries = Directory
            .EnumerateFiles(FixtureFiles.FixtureRoot(), "*.xaml", SearchOption.AllDirectories)
            .Count(f => !File.ReadAllText(f).Contains("x:Class", StringComparison.Ordinal));

        // A document that stops matching AdditionalFiles is dropped with the build still green.
        await Assert
            .That(XamlGenerated.NoesisToolkitEquivalenceTests.XamlRegistry.Dictionaries.Length)
            .IsEqualTo(dictionaries);
    }

    [Test]
    public async Task An_application_value_compares_by_its_type_not_its_ToString()
    {
        Live();

        static Border Stamped() => new() { Tag = new Stamp(), Child = new TextBlock() };

        await Assert.That(XamlParity.Compare("Stamped", Stamped(), Stamped())?.ToString()).IsNull();
    }

    [Test]
    public async Task An_attached_value_is_compared()
    {
        Live();

        static Grid Placed(int row)
        {
            var border = new Border();
            Grid.SetRow(border, row);
            return new Grid { Children = { border } };
        }

        await Assert.That(XamlParity.Compare("Placed", Placed(0), Placed(1))).IsNotNull();
    }

    [Test]
    public async Task A_nested_object_is_compared_by_its_values()
    {
        Live();

        static Border Shaded(Color stop) =>
            new()
            {
                Background = new LinearGradientBrush
                {
                    GradientStops = { new GradientStop { Color = stop } },
                },
                Child = new Border(),
            };

        await Assert
            .That(XamlParity.Compare("Shaded", Shaded(Colors.Red), Shaded(Colors.Blue)))
            .IsNotNull();

        static ResourceDictionary Setter(string stop) =>
            Dictionary(
                $"""
                <Style x:Key="Shaded" TargetType="Border">
                  <Setter Property="Background">
                    <Setter.Value><LinearGradientBrush><GradientStop Color="{stop}" /></LinearGradientBrush></Setter.Value>
                  </Setter>
                </Style>
                """
            );

        await Assert.That(XamlParity.Compare("Setter", Setter("Red"), Setter("Blue"))).IsNotNull();
    }

    [Test]
    public async Task Grid_definitions_inlines_and_resources_are_compared()
    {
        Live();

        static Grid Columns(int count)
        {
            var grid = new Grid { Children = { new Border() } };
            for (var i = 0; i < count; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            return grid;
        }

        await Assert.That(XamlParity.Compare("Columns", Columns(1), Columns(3))).IsNotNull();

        static Border Worded(Inline inline)
        {
            var text = new TextBlock();
            text.Inlines.Add(inline);
            return new Border { Child = text };
        }

        await Assert
            .That(
                XamlParity.Compare(
                    "Worded",
                    Worded(new Bold(new Run("a"))),
                    Worded(new Italic(new Run("a")))
                )
            )
            .IsNotNull();

        static Border Keyed(Color color)
        {
            var border = new Border { Child = new Border() };
            border.Resources["Fill"] = new SolidColorBrush(color);
            return border;
        }

        await Assert
            .That(XamlParity.Compare("Keyed", Keyed(Colors.Red), Keyed(Colors.Blue)))
            .IsNotNull();
    }

    [Test]
    public async Task A_root_with_nothing_below_it_is_reported()
    {
        Live();

        await Assert.That(XamlParity.Compare("Bare", new Border(), new Border())).IsNotNull();
    }

    [Test]
    public async Task Trigger_binding_and_base_style_fields_are_compared()
    {
        Live();

        static ResourceDictionary Multi(string enabled) =>
            Dictionary(
                $"""
                <Style x:Key="Multi" TargetType="Border">
                  <Style.Triggers>
                    <MultiTrigger>
                      <MultiTrigger.Conditions>
                        <Condition Property="IsMouseOver" Value="True" />
                        <Condition Property="IsEnabled" Value="{enabled}" />
                      </MultiTrigger.Conditions>
                      <Setter Property="Opacity" Value="0.5" />
                    </MultiTrigger>
                  </Style.Triggers>
                </Style>
                """
            );

        await Assert.That(XamlParity.Compare("Multi", Multi("True"), Multi("False"))).IsNotNull();

        static ResourceDictionary Fallback(int value) =>
            Dictionary(
                $$"""
                <Style x:Key="Fallback" TargetType="Border">
                  <Setter Property="Tag" Value="{Binding Missing, FallbackValue={{value}}}" />
                </Style>
                """
            );

        await Assert.That(XamlParity.Compare("Fallback", Fallback(1), Fallback(2))).IsNotNull();

        static ResourceDictionary NullValue(int value) =>
            Dictionary(
                $$"""
                <Style x:Key="NullValue" TargetType="Border">
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding Missing, TargetNullValue={{value}}}" Value="1">
                      <Setter Property="Opacity" Value="0.5" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
                """
            );

        await Assert.That(XamlParity.Compare("NullValue", NullValue(1), NullValue(2))).IsNotNull();

        static ResourceDictionary Based(string opacity) =>
            Dictionary(
                $"""
                <Style x:Key="Based" TargetType="Border">
                  <Style.BasedOn>
                    <Style TargetType="Border"><Setter Property="Opacity" Value="{opacity}" /></Style>
                  </Style.BasedOn>
                </Style>
                """
            );

        await Assert.That(XamlParity.Compare("Based", Based("0.5"), Based("0.6"))).IsNotNull();
    }

    [Test]
    public async Task A_tree_deeper_than_the_walk_is_reported()
    {
        Live();

        static Border Nested(int depth)
        {
            var root = new Border();
            var at = root;
            for (var i = 0; i < depth; i++)
            {
                var next = new Border();
                at.Child = next;
                at = next;
            }

            return root;
        }

        await Assert.That(XamlParity.Compare("Nested", Nested(40), Nested(40))).IsNotNull();
    }

    static ResourceDictionary Dictionary(string entries) =>
        (ResourceDictionary)
            GUI.ParseXaml(
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" "
                    + "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"
                    + entries
                    + "</ResourceDictionary>"
            );

    sealed class Stamp
    {
        readonly Guid _id = Guid.NewGuid();

        public override string ToString() => $"Stamp[{_id}]";
    }

    static string Dumps => Path.Combine(Path.GetTempPath(), "noesistoolkit-equivalence");
}
