using System.Globalization;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class StSub
{
    public string Name { get; set; } = "sub-name";
}

public sealed class StModel
{
    public string Name { get; set; } = "model-name";

    public StSub Child { get; } = new StSub();

    public StModel Me => this;

    public StModel[] Models => [this];
}

public sealed class StHost : Control { }

public sealed class StChildOfConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => (value as StModel)?.Child;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => null;
}

public sealed class StPickSubConverter : IMultiValueConverter
{
    public object? Convert(
        object?[] values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) =>
        values.Length > 0 && values[0] is StSub sub
            ? sub
            : "not-a-sub:" + values[0]?.GetType().Name;

    public object?[]? ConvertBack(
        object? value,
        Type[] targetTypes,
        object? parameter,
        CultureInfo culture
    ) => null;
}

public partial class StRedirectFixture : UserControl { }

[NotInParallel("Noesis")]
public sealed class CompiledSourceTypingTests
{
    [Test]
    public async Task A_templated_parent_inside_a_nested_data_template_is_the_presenter_not_the_outer_host()
    {
        NoesisRuntime.Start();

        var compiled = Nested(
            (ControlTemplate)
                XamlGenerated.NoesisToolkitEquivalenceTests.FixturesStTemplatedxamlXaml.Build()[
                    "Nested"
                ]
        );
        var parsed = Nested(
            (ControlTemplate)
                ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/StTemplated.xaml"))["Nested"]
        );

        await Assert.That(Text(parsed, "Direct")).IsEqualTo("stated");
        await Assert.That(Text(compiled, "Direct")).IsEqualTo(Text(parsed, "Direct"));
        await Assert.That(Bound(compiled, "Direct", TextBlock.TextProperty)).IsFalse();

        await Assert.That(Text(parsed, "ByRelative")).IsEqualTo("");
        await Assert.That(Text(compiled, "ByRelative")).IsEqualTo(Text(parsed, "ByRelative"));
        await Assert.That(Bound(compiled, "ByRelative", TextBlock.TextProperty)).IsTrue();

        var width = TreeSearch.Named<Border>(parsed, "Counted")!.Width;
        await Assert.That(double.IsNaN(width)).IsTrue();
        await Assert
            .That(double.IsNaN(TreeSearch.Named<Border>(compiled, "Counted")!.Width))
            .IsTrue();
        await Assert.That(Bound(compiled, "Counted", FrameworkElement.WidthProperty)).IsTrue();
    }

    [Test]
    public async Task A_templated_parent_inside_an_untyped_nested_control_template_stays_native()
    {
        NoesisRuntime.Start();

        var compiled = Untyped(
            (ControlTemplate)
                XamlGenerated.NoesisToolkitEquivalenceTests.FixturesStUntypedxamlXaml.Build()[
                    "Outer"
                ]
        );
        var parsed = Untyped(
            (ControlTemplate)
                ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/StUntyped.xaml"))["Outer"]
        );

        await Assert.That(Text(parsed, "Untyped")).IsEqualTo("");
        await Assert.That(Text(compiled, "Untyped")).IsEqualTo(Text(parsed, "Untyped"));
        await Assert
            .That(Local(compiled, "Untyped", TextBlock.TextProperty))
            .IsEqualTo(Local(parsed, "Untyped", TextBlock.TextProperty));
    }

    static string? Local(FrameworkElement root, string name, DependencyProperty property) =>
        TreeSearch.Named<FrameworkElement>(root, name)!.ReadLocalValue(property)?.GetType().Name;

    [Test]
    public async Task A_templated_parent_data_context_is_typed_from_the_template_not_the_redirecting_element()
    {
        NoesisRuntime.Start();

        var compiled = Redirected(
            (ControlTemplate)
                XamlGenerated.NoesisToolkitEquivalenceTests.FixturesStTemplatedxamlXaml.Build()[
                    "Redirected"
                ]
        );
        var parsed = Redirected(
            (ControlTemplate)
                ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/StTemplated.xaml"))[
                    "Redirected"
                ]
        );

        var native = TreeSearch.Named<Border>(parsed, "Redirected")!.Tag;
        await Assert.That(native).IsEqualTo("model-name");
        await Assert.That(TreeSearch.Named<Border>(compiled, "Redirected")!.Tag).IsEqualTo(native);
        await Assert.That(Bound(compiled, "Redirected", FrameworkElement.TagProperty)).IsFalse();
    }

    [Test]
    [Arguments("Converted")]
    [Arguments("Knob")]
    [Arguments("Resource")]
    [Arguments("FromElement")]
    [Arguments("PropertyElement")]
    [Arguments("Hosted")]
    public async Task A_data_context_the_compiler_cannot_read_leaves_what_is_below_it_native(
        string name
    )
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize();

        await Assert.That(Text(parsed, name)).IsEqualTo("sub-name");
        await Assert.That(Text(compiled, name)).IsEqualTo(Text(parsed, name));
        await Assert.That(Bound(compiled, name, TextBlock.TextProperty)).IsTrue();
    }

    [Test]
    [Arguments("Plain", "model-name")]
    [Arguments("Redirected", "sub-name")]
    public async Task A_data_context_the_compiler_can_read_still_compiles(
        string name,
        string expected
    )
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize();

        await Assert.That(Text(parsed, name)).IsEqualTo(expected);
        await Assert.That(Text(compiled, name)).IsEqualTo(Text(parsed, name));
        await Assert.That(Bound(compiled, name, TextBlock.TextProperty)).IsFalse();
    }

    [Test]
    public async Task A_keyed_template_applied_from_outside_its_document_reads_what_it_is_given()
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize();

        var compiledRow = Applied((DataTemplate)compiled.Resources["Row"]);
        var parsedRow = Applied((DataTemplate)parsed.Resources["Row"]);

        await Assert.That(parsedRow.Text).IsEqualTo("sub-name");
        await Assert.That(compiledRow.Text).IsEqualTo(parsedRow.Text);
        await Assert
            .That(BindingOperations.GetBindingExpressionBase(compiledRow, TextBlock.TextProperty))
            .IsNotNull();

        await Assert.That(Text(parsed, "RowName")).IsEqualTo("model-name");
        await Assert.That(Text(compiled, "RowName")).IsEqualTo(Text(parsed, "RowName"));
    }

    [Test]
    public async Task A_multi_binding_on_a_presenter_reads_the_content_it_adopts()
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize();
        var native = TreeSearch.Named<ContentPresenter>(parsed, "PresenterMulti")!.Tag;

        await Assert.That(native).IsEqualTo("sub-name");
        await Assert
            .That(TreeSearch.Named<ContentPresenter>(compiled, "PresenterMulti")!.Tag)
            .IsEqualTo(native);
        await Assert.That(Bound(compiled, "PresenterMulti", FrameworkElement.TagProperty)).IsTrue();
    }

    [Test]
    public async Task A_multi_binding_into_the_data_context_reads_its_own_result()
    {
        NoesisRuntime.Start();

        var compiled = Hosted(
            (DataTemplate)
                XamlGenerated.NoesisToolkitEquivalenceTests.FixturesStTemplatedxamlXaml.Build()[
                    "ContextMulti"
                ]
        );
        var parsed = Hosted(
            (DataTemplate)
                ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/StTemplated.xaml"))[
                    "ContextMulti"
                ]
        );
        var native = TreeSearch.Named<Border>(parsed, "ContextMulti")!.DataContext;

        await Assert.That(native).IsEqualTo("not-a-sub:NamedObject");
        await Assert
            .That(TreeSearch.Named<Border>(compiled, "ContextMulti")!.DataContext)
            .IsEqualTo(native);
        await Assert
            .That(Bound(compiled, "ContextMulti", FrameworkElement.DataContextProperty))
            .IsTrue();
    }

    [Test]
    [Arguments("LowerOff", true)]
    [Arguments("UpperOff", true)]
    [Arguments("Unparsed", true)]
    [Arguments("LowerOn", false)]
    public async Task The_compile_bindings_marker_reads_as_a_boolean(string name, bool native)
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize();

        await Assert.That(Text(parsed, name)).IsEqualTo("model-name");
        await Assert.That(Text(compiled, name)).IsEqualTo(Text(parsed, name));
        await Assert.That(Bound(compiled, name, TextBlock.TextProperty)).IsEqualTo(native);
    }

    static (StRedirectFixture Compiled, StRedirectFixture Parsed) Realize()
    {
        var compiled = new StRedirectFixture { DataContext = new StModel() };
        compiled.InitializeComponent();

        var parsed = (StRedirectFixture)GUI.LoadXaml("/Fixtures;Fixtures/StRedirect.xaml");
        parsed.DataContext = new StModel();

        NoesisRuntime.Show(new Grid { Width = 800, Height = 900 }, compiled, parsed);
        return (compiled, parsed);
    }

    static TextBlock Applied(DataTemplate template)
    {
        var items = new ItemsControl
        {
            ItemsSource = new[] { new StSub() },
            ItemTemplate = template,
        };
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, items);
        return TreeSearch.Named<TextBlock>(items, "RowName")!;
    }

    static SpikeControl Nested(ControlTemplate template)
    {
        var host = new SpikeControl
        {
            Template = template,
            Tag = new List<string> { "a" },
            Label = "stated",
            Count = 7,
        };
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
        return host;
    }

    static SpikeControl Untyped(ControlTemplate template)
    {
        var host = new SpikeControl { Template = template, Label = "stated" };
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
        return host;
    }

    static ContentControl Hosted(DataTemplate template)
    {
        var host = new ContentControl { Content = new StModel(), ContentTemplate = template };
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
        return host;
    }

    static StHost Redirected(ControlTemplate template)
    {
        var host = new StHost { Template = template, DataContext = new StModel() };
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
        return host;
    }

    static string? Text(FrameworkElement root, string name) =>
        TreeSearch.Named<TextBlock>(root, name)?.Text;

    static bool Bound(FrameworkElement root, string name, DependencyProperty property) =>
        BindingOperations.GetBindingExpressionBase(
            TreeSearch.Named<FrameworkElement>(root, name)!,
            property
        )
            is not null;
}
