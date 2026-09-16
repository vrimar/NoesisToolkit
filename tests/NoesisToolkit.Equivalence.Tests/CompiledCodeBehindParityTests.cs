using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public partial class CbParsedNamesRoot : UserControl { }

public partial class CbLoaderRoot : UserControl
{
    public int Clicks;

    void OnAnyClick(object sender, RoutedEventArgs e) => Clicks++;
}

[NotInParallel("Noesis")]
public sealed class CompiledCodeBehindParityTests
{
    static (CbParsedNamesRoot Native, CbParsedNamesRoot Compiled) ParsedNames()
    {
        NoesisRuntime.Start();

        var native = (CbParsedNamesRoot)GUI.LoadXaml("/Fixtures;Fixtures/CbParsedNames.xaml");

        var compiled = new CbParsedNamesRoot();
        compiled.InitializeComponent();

        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 600 }, native, compiled);
        return (native, compiled);
    }

    [Test]
    [Arguments("HeaderText")]
    [Arguments("Wrap")]
    [Arguments("Go")]
    public async Task A_name_inside_a_parsed_subtree_is_found_from_the_root(string name)
    {
        var (native, compiled) = ParsedNames();

        await Assert.That(native.FindName(name)).IsNotNull();
        await Assert.That(compiled.FindName(name)).IsNotNull();
    }

    [Test]
    public async Task A_name_inside_a_parsed_subtree_has_an_accessor_holding_that_element()
    {
        var (_, compiled) = ParsedNames();

        await Assert.That(compiled.HeaderText).IsNotNull();
        await Assert.That(compiled.HeaderText).IsSameReferenceAs(compiled.FindName("HeaderText"));
        await Assert.That(compiled.Go).IsNotNull();
        await Assert.That(compiled.Go).IsSameReferenceAs(compiled.FindName("Go"));
    }

    [Test]
    [Arguments("HeaderEcho", "header")]
    [Arguments("GoEcho", "30")]
    public async Task An_element_name_binding_reaches_into_a_parsed_subtree(
        string name,
        string expected
    )
    {
        var (native, compiled) = ParsedNames();

        await Assert.That(((TextBlock)native.FindName(name)).Text).IsEqualTo(expected);
        await Assert.That(((TextBlock)compiled.FindName(name)).Text).IsEqualTo(expected);
    }

    [Test]
    public async Task An_element_name_binding_in_a_template_reaches_into_a_parsed_subtree()
    {
        var (native, compiled) = ParsedNames();

        static string? Echo(FrameworkElement root) =>
            TreeSearch
                .Named<TextBlock>((ContentControl)root.FindName("Templated"), "InnerEcho")
                ?.Text;

        await Assert.That(Echo(native)).IsEqualTo("inner");
        await Assert.That(Echo(compiled)).IsEqualTo("inner");
    }

    [Test]
    public async Task An_attached_event_handler_runs_when_the_event_routes_through()
    {
        NoesisRuntime.Start();

        var native = (CbLoaderRoot)GUI.LoadXaml("/Fixtures;Fixtures/CbLoader.xaml");
        var compiled = new CbLoaderRoot();
        compiled.InitializeComponent();
        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, native, compiled);

        Click((Button)native.FindName("Target"));
        Click(compiled.Target);

        await Assert.That(native.Clicks).IsEqualTo(1);
        await Assert.That(compiled.Clicks).IsEqualTo(1);
    }

    [Test]
    public async Task A_reloaded_root_hands_out_the_element_the_reload_built()
    {
        NoesisRuntime.Start();

        var native = new CbLoaderRoot();
        GUI.LoadComponent(native, "/Fixtures;Fixtures/CbLoader.xaml");
        var nativeFirst = native.FindName("Target");
        GUI.LoadComponent(native, "/Fixtures;Fixtures/CbLoader.xaml");
        await Assert.That(native.FindName("Target")).IsNotNull();
        await Assert.That(native.FindName("Target")).IsNotSameReferenceAs(nativeFirst);

        var compiled = new CbLoaderRoot();
        compiled.InitializeComponent();
        var compiledFirst = compiled.Target;
        compiled.InitializeComponent();

        await Assert.That(compiled.Target).IsNotSameReferenceAs(compiledFirst);
        await Assert.That(compiled.Target).IsSameReferenceAs(compiled.FindName("Target"));
    }

    static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
}
