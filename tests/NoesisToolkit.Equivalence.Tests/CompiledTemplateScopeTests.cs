using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public class TsHost : Control
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        "Ratio",
        typeof(double),
        typeof(TsHost),
        new FrameworkPropertyMetadata(0.0)
    );

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        "Mode",
        typeof(int),
        typeof(TsHost),
        new FrameworkPropertyMetadata(0)
    );

    public int Mode
    {
        get => (int)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }
}

public class TsPart : Control
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        "Ratio",
        typeof(double),
        typeof(TsPart),
        new FrameworkPropertyMetadata(0.0)
    );

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }
}

public sealed class TsInner
{
    public string Label { get; set; } = "";
}

[NotInParallel("Noesis")]
public sealed class CompiledTemplateScopeTests
{
    static ResourceDictionary Compiled() =>
        XamlGenerated.NoesisToolkitEquivalenceTests.FixturesTsScopexamlXaml.Build();

    static ResourceDictionary Parsed() =>
        (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/TsScope.xaml");

    static SpikeControl Spike(
        ResourceDictionary dictionary,
        string key,
        Panel root,
        Action<SpikeControl>? setup = null
    )
    {
        var host = new SpikeControl { Width = 100, Height = 100 };
        setup?.Invoke(host);
        host.Template = (ControlTemplate)dictionary[key];
        root.Width = 400;
        root.Height = 300;
        NoesisRuntime.Show(root, host);
        return host;
    }

    static SpikeItem Nested() =>
        new SpikeItem
        {
            Label = "outer",
            Payload = new TsInner { Label = "inner" },
        };

    static async Task<Border> Matches(string key, float expected)
    {
        var compiled = Spike(Compiled(), key, new Grid(), h => h.DataContext = Nested());
        var native = Spike(Parsed(), key, new Grid(), h => h.DataContext = Nested());
        var nativeOpacity = TreeSearch.Named<Border>(native, "Bg")!.Opacity;
        var compiledBg = TreeSearch.Named<Border>(compiled, "Bg")!;

        await Assert.That(nativeOpacity).IsEqualTo(expected);
        await Assert.That(compiledBg.Opacity).IsEqualTo(nativeOpacity);
        return compiledBg;
    }

    [Test]
    [Arguments("RedirectedContext", "InheritedContext", 1f)]
    [Arguments("RedirectedSelfContext", "InheritedSelfContext", 0.5f)]
    public async Task A_data_condition_reads_the_templated_parents_context_not_the_roots(
        string redirected,
        string inherited,
        float expected
    )
    {
        NoesisRuntime.Start();

        var redirectedBg = await Matches(redirected, expected);
        var inheritedBg = await Matches(inherited, 0.5f);

        await Assert.That(CompiledTriggerSet.SetsOf(redirectedBg)).IsEmpty();
        await Assert.That(CompiledTriggerSet.SetsOf(inheritedBg)).IsNotEmpty();
    }

    [Test]
    public async Task A_templated_parent_condition_does_not_read_the_control_the_template_is_on()
    {
        NoesisRuntime.Start();

        var bg = await Matches("TemplatedParentCondition", 1f);

        await Assert.That(CompiledTriggerSet.SetsOf(bg)).IsEmpty();
        await Assert
            .That(CompiledTriggerSet.SetsOf(await Matches("InheritedSelfContext", 0.5f)))
            .IsNotEmpty();
    }

    [Test]
    public async Task An_ancestor_condition_starts_its_walk_above_the_templated_parent()
    {
        NoesisRuntime.Start();

        var compiled = Spike(
            Compiled(),
            "AncestorStart",
            new StackPanel { Orientation = Orientation.Horizontal }
        );
        var native = Spike(
            Parsed(),
            "AncestorStart",
            new StackPanel { Orientation = Orientation.Horizontal }
        );
        var compiledBg = TreeSearch.Named<Border>(compiled, "Bg")!;
        var nativeBg = TreeSearch.Named<Border>(native, "Bg")!;

        await Assert.That(nativeBg.Opacity).IsEqualTo(1f);
        await Assert.That(compiledBg.Opacity).IsEqualTo(nativeBg.Opacity);

        await Assert.That(nativeBg.Tag).IsEqualTo("found");
        await Assert.That(compiledBg.Tag).IsEqualTo(nativeBg.Tag);

        await Assert.That(CompiledTriggerSet.SetsOf(compiledBg)).IsNotEmpty();
        await Assert.That(CompiledTriggerSet.SetsOf(compiledBg)[0].Specs.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_target_name_resolves_past_a_same_name_in_a_nested_template()
    {
        NoesisRuntime.Start();

        var compiled = Spike(
            Compiled(),
            "NestedName",
            new Grid(),
            h => h.Align = VerticalAlignment.Top
        );
        var native = Spike(
            Parsed(),
            "NestedName",
            new Grid(),
            h => h.Align = VerticalAlignment.Top
        );

        var nativeColor = (
            (SolidColorBrush)TreeSearch.Named<Border>(native, "Bg")!.Background
        ).Color;
        await Assert.That(nativeColor).IsEqualTo(Colors.Green);
        await Assert
            .That(((SolidColorBrush)TreeSearch.Named<Border>(compiled, "Bg")!.Background).Color)
            .IsEqualTo(nativeColor);

        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.All<StackPanel>(compiled).First()))
            .IsNotEmpty();
    }

    [Test]
    public async Task A_parsed_setter_resolves_its_target_name_past_a_nested_template()
    {
        NoesisRuntime.Start();

        var dictionary =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesTsCollidexamlXaml.Build();
        var parsed = (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/TsCollide.xaml");

        var compiled = new ContentControl
        {
            Template = (ControlTemplate)dictionary["Collide"],
            Tag = "on",
            Content = "c",
        };
        var native = new ContentControl
        {
            Template = (ControlTemplate)parsed["Collide"],
            Tag = "on",
            Content = "c",
        };
        NoesisRuntime.Show(compiled, native);

        var nativePadding = TreeSearch.Named<Border>(native, "Part")!.Padding;
        await Assert.That(nativePadding.Left).IsEqualTo(7f);
        await Assert
            .That(TreeSearch.Named<Border>(compiled, "Part")!.Padding)
            .IsEqualTo(nativePadding);

        var setter = (Setter)
            ((Trigger)((ControlTemplate)dictionary["Collide"]).Triggers[0]).Setters[0];
        await Assert.That(setter.Property).IsSameReferenceAs(Border.PaddingProperty);
    }

    [Test]
    public async Task A_probed_setter_resolves_its_property_against_the_named_element()
    {
        NoesisRuntime.Start();

        var dictionary = Compiled();
        TsHost Host(ResourceDictionary source)
        {
            var host = new TsHost
            {
                Width = 100,
                Height = 100,
                Ratio = 0.25,
                Template = (ControlTemplate)source["ProbedTarget"],
            };
            var view = NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
            host.Mode = 2;
            NoesisRuntime.Pump(view, host);
            return host;
        }

        var compiled = Host(dictionary);
        var native = Host(Parsed());

        var nativeRatio = TreeSearch.Named<TsPart>(native, "Part")!.Ratio;
        await Assert.That(nativeRatio).IsEqualTo(0.25);
        await Assert.That(TreeSearch.Named<TsPart>(compiled, "Part")!.Ratio).IsEqualTo(nativeRatio);

        var setter = (Setter)
            ((Trigger)((ControlTemplate)dictionary["ProbedTarget"]).Triggers[0]).Setters[0];
        await Assert.That(setter.Property).IsSameReferenceAs(TsPart.RatioProperty);
    }

    [Test]
    public async Task A_probed_setter_naming_a_noesis_element_resolves_its_property()
    {
        NoesisRuntime.Start();

        var dictionary = Compiled();
        ContentControl Host(ResourceDictionary source)
        {
            var host = new ContentControl
            {
                ContentTemplate = (DataTemplate)source["ProbedNoesisTarget"],
                Content = new SpikeItem { Flag = true, Label = "nm" },
                Width = 100,
                Height = 50,
            };
            NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
            return host;
        }

        var compiled = Host(dictionary);
        var native = Host(Parsed());

        var nativeTag = TreeSearch.Named<Border>(native, "Bd")!.Tag;
        await Assert.That(nativeTag).IsEqualTo("nm");
        await Assert.That(TreeSearch.Named<Border>(compiled, "Bd")!.Tag).IsEqualTo(nativeTag);

        var setter = (Setter)
            ((DataTrigger)((DataTemplate)dictionary["ProbedNoesisTarget"]).Triggers[0]).Setters[0];
        await Assert.That(setter.Property).IsSameReferenceAs(FrameworkElement.TagProperty);
    }
}
