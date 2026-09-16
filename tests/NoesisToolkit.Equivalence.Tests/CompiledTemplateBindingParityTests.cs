using System.Globalization;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledTemplateBindingParityTests
{
    sealed class Side(ResourceDictionary resources)
    {
        internal readonly List<string> Warnings = new();
        internal readonly ResourceDictionary Resources = resources;
        internal readonly StackPanel Root = new() { Width = 400, Height = 600 };
        internal View View = null!;

        internal void Show(FrameworkElement host)
        {
            Log.SetLogCallback(
                (level, _, message) =>
                {
                    if (level is LogLevel.Warning or LogLevel.Error)
                        Warnings.Add(message);
                }
            );
            View = NoesisRuntime.Show(Root, host);
            Log.SetLogCallback(null);
        }

        internal void Pump() => NoesisRuntime.Pump(View, Root);

        internal T Named<T>(string name)
            where T : FrameworkElement => TreeSearch.Named<T>(Root, name)!;
    }

    static (Side Native, Side Compiled) Sides()
    {
        NoesisRuntime.Start();
        return (
            new Side((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/TbParity.xaml")),
            new Side(XamlGenerated.NoesisToolkitEquivalenceTests.FixturesTbParityxamlXaml.Build())
        );
    }

    static string Local(DependencyObject element, DependencyProperty property) =>
        $"{element.ReadLocalValue(property)?.GetType().Name}/"
        + $"{BindingOperations.GetBindingExpressionBase(element, property) is not null}";

    [Test]
    public async Task A_template_binding_into_a_two_way_slot_never_writes_the_templated_parent()
    {
        var (native, compiled) = Sides();
        var hosts = new List<ToggleButton>();

        foreach (var side in new[] { native, compiled })
        {
            var host = new ToggleButton
            {
                Template = (ControlTemplate)side.Resources["Toggle"],
                IsChecked = false,
            };
            hosts.Add(host);
            side.Show(host);
            side.Named<CheckBox>("Check").IsChecked = true;
            side.Pump();
        }

        await Assert.That(hosts[0].IsChecked).IsEqualTo(false);
        await Assert.That(hosts[1].IsChecked).IsEqualTo(hosts[0].IsChecked);

        foreach (var (side, host) in new[] { (native, hosts[0]), (compiled, hosts[1]) })
        {
            host.IsChecked = null;
            side.Pump();
        }

        await Assert
            .That(compiled.Named<CheckBox>("Check").IsChecked)
            .IsEqualTo(native.Named<CheckBox>("Check").IsChecked);
        await Assert
            .That(Local(compiled.Named<CheckBox>("Check"), ToggleButton.IsCheckedProperty))
            .IsEqualTo(Local(native.Named<CheckBox>("Check"), ToggleButton.IsCheckedProperty));
    }

    [Test]
    public async Task A_template_binding_converts_nothing_and_warns_as_the_parser_does()
    {
        var (native, compiled) = Sides();
        var hosts = new List<ToggleButton>();

        foreach (var side in new[] { native, compiled })
        {
            var host = new ToggleButton
            {
                Template = (ControlTemplate)side.Resources["Toggle"],
                Width = 42,
                Opacity = 0.5f,
                TabIndex = 3,
            };
            hosts.Add(host);
            side.Show(host);
        }

        string Shape(Side side)
        {
            var brushed = side.Named<Border>("Brushed");
            return $"text='{side.Named<TextBlock>("Number").Text}' "
                + $"opacity={((SolidColorBrush)brushed.Background).Opacity.ToString(CultureInfo.InvariantCulture)} "
                + $"z={Panel.GetZIndex(brushed)} "
                + $"local={Local(side.Named<TextBlock>("Number"), TextBlock.TextProperty)}";
        }

        await Assert.That(Shape(native)).IsEqualTo("text='' opacity=1 z=3 local=NamedObject/False");
        await Assert.That(Shape(compiled)).IsEqualTo(Shape(native));
        await Assert.That(compiled.Warnings).IsEquivalentTo(native.Warnings);

        foreach (var (side, host) in new[] { (native, hosts[0]), (compiled, hosts[1]) })
        {
            host.Opacity = 0.25f;
            host.TabIndex = 7;
            side.Pump();
        }

        await Assert.That(Shape(compiled)).IsEqualTo(Shape(native));
    }

    [Test]
    public async Task A_template_binding_between_managed_properties_and_into_an_inline_follows_the_host()
    {
        var (native, compiled) = Sides();
        var hosts = new List<SpikeControl>();

        foreach (var side in new[] { native, compiled })
        {
            var host = new SpikeControl
            {
                Template = (ControlTemplate)side.Resources["Managed"],
                Label = "first",
                Count = 4,
            };
            hosts.Add(host);
            side.Show(host);
        }

        string Shape(Side side)
        {
            var inner = side.Named<SpikeControl>("Inner");
            var word = (Run)side.Named<TextBlock>("Inline").Inlines.First();
            return $"label={inner.Label} count={inner.Count} word={word.Text} "
                + $"counted='{side.Named<TextBlock>("Counted").Text}' "
                + $"local={Local(inner, SpikeControl.LabelProperty)}";
        }

        await Assert
            .That(Shape(native))
            .IsEqualTo("label=first count=4 word=first counted='' local=NamedObject/False");
        await Assert.That(Shape(compiled)).IsEqualTo(Shape(native));

        foreach (var (side, host) in new[] { (native, hosts[0]), (compiled, hosts[1]) })
        {
            host.Label = "second";
            host.Count = 9;
            side.Pump();
        }

        await Assert.That(Shape(compiled)).IsEqualTo(Shape(native));
        await Assert.That(compiled.Warnings).IsEquivalentTo(native.Warnings);
    }

    [Test]
    public async Task A_control_template_in_a_style_setter_binds_against_the_style_type()
    {
        var (native, compiled) = Sides();

        foreach (var side in new[] { native, compiled })
            side.Show(
                new SpikeControl { Style = (Style)side.Resources["Styled"], Label = "styled" }
            );

        await Assert.That(native.Named<TextBlock>("FromStyle").Text).IsEqualTo("styled");
        await Assert.That(compiled.Named<TextBlock>("FromStyle").Text).IsEqualTo("styled");
        await Assert.That(compiled.Warnings).IsEquivalentTo(native.Warnings);
    }

    [Test]
    public async Task Data_and_panel_templates_keep_the_parsers_template_binding()
    {
        var (native, compiled) = Sides();

        foreach (var side in new[] { native, compiled })
        {
            side.Show(
                new ContentControl
                {
                    Content = "item",
                    Tag = "outer",
                    ContentTemplate = (DataTemplate)side.Resources["Data"],
                }
            );
            side.Root.Children.Add(
                new ItemsControl
                {
                    ItemsPanel = (ItemsPanelTemplate)side.Resources["Panel"],
                    ItemsSource = new[] { "a" },
                    Margin = new Thickness(3),
                }
            );
            side.Pump();
        }

        string Shape(Side side)
        {
            var inData = side.Named<TextBlock>("InData");
            var stack = side.Named<StackPanel>("Stack");
            return $"tag={inData.Tag ?? "null"} {Local(inData, FrameworkElement.TagProperty)} "
                + $"margin={stack.Margin.Left.ToString(CultureInfo.InvariantCulture)} {Local(stack, FrameworkElement.MarginProperty)}";
        }

        await Assert.That(Shape(compiled)).IsEqualTo(Shape(native));
        await Assert.That(compiled.Warnings).IsEquivalentTo(native.Warnings);
    }
}
