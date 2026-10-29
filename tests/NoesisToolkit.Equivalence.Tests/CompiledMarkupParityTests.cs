using System.Globalization;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public static class MkMarker
{
    public static readonly DependencyProperty PayloadProperty = DependencyProperty.RegisterAttached(
        "Payload",
        typeof(object),
        typeof(MkMarker),
        new PropertyMetadata(null)
    );

    public static object? GetPayload(DependencyObject target) => target.GetValue(PayloadProperty);

    public static void SetPayload(DependencyObject target, object? value) =>
        target.SetValue(PayloadProperty, value);

    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill",
        typeof(Brush),
        typeof(MkMarker),
        new PropertyMetadata(null)
    );

    public static Brush? GetFill(DependencyObject target) => (Brush?)target.GetValue(FillProperty);

    public static void SetFill(DependencyObject target, Brush? value) =>
        target.SetValue(FillProperty, value);
}

public partial class MkNativeRoot : UserControl { }

public partial class MkQualifiedRoot : UserControl { }

public sealed class MkItem
{
    public string Name { get; set; } = "";

    public object? Nothing { get; set; }
}

public sealed class MkParameterConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => MkDescribe.Value(parameter);

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

public sealed class MkMultiParameterConverter : IMultiValueConverter
{
    public object? Convert(
        object?[] values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) =>
        parameter switch
        {
            "unset" => DependencyProperty.UnsetValue,
            "null" => null,
            _ => MkDescribe.Value(parameter),
        };

    public object?[]? ConvertBack(
        object? value,
        Type[] targetTypes,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

static class MkDescribe
{
    internal static string Value(object? value) =>
        value switch
        {
            null => "<null>",
            SolidColorBrush brush => $"brush {brush.Color}",
            _ => $"{value.GetType().Name} {value}",
        };
}

public partial class MkMarkupRoot : UserControl { }

[NotInParallel("Noesis")]
public sealed class CompiledMarkupParityTests
{
    const string Brush = "brush #FF123456";

    static (MkMarkupRoot Native, MkMarkupRoot Compiled) Roots()
    {
        NoesisRuntime.Start();

        var native = (MkMarkupRoot)GUI.LoadXaml("/Fixtures;Fixtures/MkMarkup.xaml");
        native.DataContext = new MkItem { Name = "context" };

        var compiled = new MkMarkupRoot { DataContext = new MkItem { Name = "context" } };
        compiled.InitializeComponent();

        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 600 }, native, compiled);
        return (native, compiled);
    }

    static T Named<T>(FrameworkElement root, string name)
        where T : FrameworkElement => (T)root.FindName(name);

    [Test]
    [Arguments("StaticPayload")]
    [Arguments("DynamicPayload")]
    public async Task A_resource_on_an_attached_property_is_the_value_it_names(string name)
    {
        var (native, compiled) = Roots();

        var expected = MkDescribe.Value(MkMarker.GetPayload(Named<Border>(native, name)));
        await Assert.That(expected).IsEqualTo(Brush);
        await Assert
            .That(MkDescribe.Value(MkMarker.GetPayload(Named<Border>(compiled, name))))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task A_resource_on_a_typed_attached_property_is_the_value_it_names()
    {
        var (native, compiled) = Roots();

        var expected = MkDescribe.Value(MkMarker.GetFill(Named<Border>(native, "TypedFill")));
        await Assert.That(expected).IsEqualTo(Brush);
        await Assert
            .That(MkDescribe.Value(MkMarker.GetFill(Named<Border>(compiled, "TypedFill"))))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task An_owner_qualified_property_on_another_type_is_the_value_it_names()
    {
        NoesisRuntime.Start();

        var native = (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/MkQualified.xaml");
        var compiled =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesMkQualifiedxamlXaml.Build();
        XamlGenerated.NoesisToolkitEquivalenceTests.XamlResources.Flush(compiled);

        foreach (var (dictionary, side) in new[] { (native, "native"), (compiled, "compiled") })
        {
            DependencyObject Entry(string key) => (DependencyObject)dictionary[key];

            await Assert
                .That(TextElement.GetFontSize(Entry("Mk.QualifiedLiteral")))
                .IsEqualTo(14f)
                .Because(side);
            await Assert
                .That(MkDescribe.Value(TextElement.GetForeground(Entry("Mk.QualifiedResource"))))
                .IsEqualTo(Brush)
                .Because(side);
            await Assert
                .That(TextElement.GetFontWeight(Entry("Mk.QualifiedEnum")))
                .IsEqualTo(FontWeight.Bold)
                .Because(side);
        }
    }

    [Test]
    [Arguments("SourceKnob", "from-resource")]
    [Arguments("ParameterKnob", Brush)]
    [Arguments("MultiNullParameter", "<null>")]
    [Arguments("MultiResourceParameter", Brush)]
    [Arguments("MultiTypeParameter", "RuntimeType Noesis.Border")]
    [Arguments("MultiFormat", "[context]")]
    [Arguments("ScopedParameter", "String inner")]
    public async Task Markup_on_a_binding_knob_renders_what_the_parser_renders(
        string name,
        string expected
    )
    {
        var (native, compiled) = Roots();

        await Assert.That(Named<TextBlock>(native, name).Text).IsEqualTo(expected);
        await Assert.That(Named<TextBlock>(compiled, name).Text).IsEqualTo(expected);
    }

    [Test]
    public async Task Text_in_an_attached_property_element_is_its_value()
    {
        var (native, compiled) = Roots();

        var expected = ToolTipService.GetToolTip(Named<Border>(native, "TextTip"));
        await Assert.That(expected).IsEqualTo("Save");
        await Assert
            .That(ToolTipService.GetToolTip(Named<Border>(compiled, "TextTip")))
            .IsEqualTo(expected);
    }

    [Test]
    [Arguments("FallbackKnob")]
    [Arguments("NullKnob")]
    [Arguments("MultiFallback")]
    [Arguments("MultiNull")]
    public async Task A_resource_standing_in_for_a_binding_value_is_the_value_it_names(string name)
    {
        var (native, compiled) = Roots();

        var expected = MkDescribe.Value(Named<Border>(native, name).Tag);
        await Assert.That(expected).IsEqualTo(Brush);
        await Assert.That(MkDescribe.Value(Named<Border>(compiled, name).Tag)).IsEqualTo(expected);
    }

    [Test]
    public async Task A_root_image_source_resolves_against_its_document()
    {
        var (native, compiled) = Roots();

        var expected = SourceUri(Named<Image>(native, "RootImage"));
        await Assert.That(expected).IsEqualTo("/Fixtures;Fixtures/img/probe.png");
        await Assert.That(SourceUri(Named<Image>(compiled, "RootImage"))).IsEqualTo(expected);
    }

    [Test]
    [Arguments("MkImageRelative", "/Fixtures;Fixtures/probe.png")]
    [Arguments("MkImageDotted", "/Fixtures;Fixtures/probe.png")]
    [Arguments("MkImageParent", "/probe.png")]
    [Arguments("MkImageBackslash", "/Fixtures;Fixtures/img/probe.png")]
    [Arguments("MkImageColonInQuery", "/Fixtures;Fixtures/probe.png?at=a:b")]
    [Arguments("MkImageRooted", "/Other;img/probe.png")]
    [Arguments("MkImageScheme", "file:///tmp/probe.png")]
    [Arguments("MkImagePack", "/Other;component/probe.png")]
    [Arguments("MkImageDrive", "C:/img/probe.png")]
    [Arguments("MkImageBrush", "/Fixtures;Fixtures/probe.png")]
    public async Task An_image_source_resolves_the_way_the_parser_resolves_it(
        string key,
        string expected
    )
    {
        NoesisRuntime.Start();

        var native = ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/MkImages.xaml"))[key];
        var compiled = XamlGenerated.NoesisToolkitEquivalenceTests.FixturesMkImagesxamlXaml.Build()[
            key
        ];

        await Assert.That(SourceUri(native)).IsEqualTo(expected);
        await Assert.That(SourceUri(compiled)).IsEqualTo(expected);
    }

    [Test]
    public async Task An_owner_qualified_property_on_the_root_s_own_type_is_the_plain_property()
    {
        NoesisRuntime.Start();

        var parsed = new MkQualifiedRoot();
        GUI.LoadComponent(parsed, "/Fixtures;Fixtures/MkQualifiedRoot.xaml");
        var compiled = new MkQualifiedRoot();
        compiled.InitializeComponent();

        await Assert.That(parsed.HorizontalContentAlignment).IsEqualTo(HorizontalAlignment.Right);
        await Assert.That(compiled.HorizontalContentAlignment).IsEqualTo(HorizontalAlignment.Right);
    }

    static Setter[] NativeSetters(ResourceDictionary dictionary) =>
        ((Style)dictionary["Mk.NativeSetters"]).Setters.Cast<Setter>().ToArray();

    static string? StrokeOf(DependencyObject target, ResourceDictionary dictionary) =>
        (
            target.GetValue(NativeSetters(dictionary)[1].Property) as SolidColorBrush
        )?.Color.ToString();

    [Test]
    public async Task A_property_of_Noesis_extensions_is_set_and_watched_as_the_parser_does()
    {
        NoesisRuntime.Start();

        var native = (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/MkNative.xaml");
        var compiled = XamlGenerated.NoesisToolkitEquivalenceTests.FixturesMkNativexamlXaml.Build();
        XamlGenerated.NoesisToolkitEquivalenceTests.XamlResources.Flush(compiled);

        foreach (var (dictionary, side) in new[] { (native, "native"), (compiled, "compiled") })
        {
            var setters = NativeSetters(dictionary);
            await Assert.That(setters[0].Property.Name).IsEqualTo("StrokeThickness").Because(side);
            await Assert.That(setters[0].Value).IsEqualTo(2f).Because(side);

            var element = (TextBlock)dictionary["Mk.NativeElement"];
            await Assert.That(StrokeOf(element, dictionary)).IsEqualTo("#FF123456").Because(side);

            var triggered = (TextBlock)dictionary["Mk.NativeTriggered"];
            dictionary.Remove("Mk.NativeTriggered");
            NoesisRuntime.Show(triggered);
            await Assert.That(triggered.Tag).IsEqualTo("trigger").Because(side);
            await Assert.That(triggered.Width).IsEqualTo(33f).Because(side);
        }
    }

    [Test]
    public async Task A_root_leaves_a_key_it_never_declares_to_the_parser_and_the_installed_resources()
    {
        NoesisRuntime.Start();

        var setters = (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/MkNative.xaml");

        async Task Check(MkNativeRoot root, string side)
        {
            root.DataContext = new MkItem { Name = "abc" };
            NoesisRuntime.Show(root);

            TextBlock Part(string name) => (TextBlock)root.FindName(name);

            await Assert.That(Part("Converted").Text).IsEqualTo("ABC").Because(side);
            await Assert
                .That(StrokeOf(Part("Stroked"), setters))
                .IsEqualTo("#FF3366CC")
                .Because(side);
            await Assert
                .That(StrokeOf(Part("WholeStroked"), setters))
                .IsEqualTo("#FF3366CC")
                .Because(side);
        }

        var parsed = new MkNativeRoot();
        GUI.LoadComponent(parsed, "/Fixtures;Fixtures/MkNativeRoot.xaml");
        var compiled = new MkNativeRoot();
        compiled.InitializeComponent();

        await Check(parsed, "parsed");
        await Check(compiled, "compiled");
    }

    static string? SourceUri(object? holder) =>
        (
            holder switch
            {
                Image image => image.Source,
                ImageBrush brush => brush.ImageSource,
                _ => null,
            }
        )
            is BitmapImage bitmap
            ? bitmap.UriSource?.OriginalString
            : null;
}
