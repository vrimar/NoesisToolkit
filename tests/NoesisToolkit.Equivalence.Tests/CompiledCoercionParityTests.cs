using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class CoModel : INotifyPropertyChanged
{
    int _tick;

    public int Tick
    {
        get => _tick;
        set
        {
            _tick = value;
            Raise();
        }
    }

    int? _maybeCount;

    public int? MaybeCount
    {
        get => _maybeCount;
        set
        {
            _maybeCount = value;
            Raise();
        }
    }

    public double? MaybeRatio { get; set; }

    public int Duration { get; set; } = 1234;

    public char Mask { get; set; } = '#';

    public GridResizeDirection Direction { get; set; } = GridResizeDirection.Rows;

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CoSwitch : IValueConverter
{
    public object? Result { get; set; }

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => Result;

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

public sealed class CoMultiSwitch : IMultiValueConverter
{
    public object? Result { get; set; }

    public object? Convert(
        object?[] values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => Result;

    public object?[] ConvertBack(
        object? value,
        Type[] targetTypes,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

public sealed class CoRecorder : IValueConverter
{
    public List<string> Seen { get; } = new List<string>();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Seen.Add($"{parameter}:{targetType.FullName}");
        return value;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

[NotInParallel("Noesis")]
public sealed class CompiledCoercionParityTests
{
    [Test]
    public async Task A_converter_result_of_unset_writes_the_default_and_do_nothing_leaves_the_slot()
    {
        NoesisRuntime.Start();

        var sides = Sides("Sentinels");
        foreach (var side in sides)
        {
            side.Switch("WidthSwitch").Result = 5f;
            side.MultiSwitch.Result = "first";
            side.Model.Tick = 1;
            side.Pump();
        }

        foreach (var side in sides)
        {
            await Assert.That(side.Find<Border>("Switched").Width).IsEqualTo(5f);
            await Assert.That(side.Find<SpikeControl>("SwitchedMulti").Label).IsEqualTo("first");
        }

        await Assert
            .That(Bound(sides[0].Find<Border>("Switched"), FrameworkElement.WidthProperty))
            .IsFalse();
        await Assert
            .That(Bound(sides[0].Find<SpikeControl>("SwitchedMulti"), SpikeControl.LabelProperty))
            .IsFalse();

        var steps = new (object? Single, object? Multi, float Width, string Label)[]
        {
            (Binding.DoNothing, Binding.DoNothing, 5f, "first"),
            (
                DependencyProperty.UnsetValue,
                DependencyProperty.UnsetValue,
                float.NaN,
                "host-default"
            ),
            (7f, "second", 7f, "second"),
            (
                DependencyProperty.UnsetValue,
                DependencyProperty.UnsetValue,
                float.NaN,
                "host-default"
            ),
            (
                DependencyProperty.UnsetValue,
                DependencyProperty.UnsetValue,
                float.NaN,
                "host-default"
            ),
        };

        foreach (var (single, multi, width, label) in steps)
        {
            foreach (var side in sides)
            {
                side.Switch("WidthSwitch").Result = single;
                side.MultiSwitch.Result = multi;
                side.Model.Tick++;
                side.Pump();

                await Assert
                    .That(side.Find<Border>("Switched").Width)
                    .IsEqualTo(width)
                    .Because(side.Name);
                await Assert
                    .That(side.Find<SpikeControl>("SwitchedMulti").Label)
                    .IsEqualTo(label)
                    .Because(side.Name);
            }
        }
    }

    [Test]
    public async Task A_null_nullable_source_leaves_the_metadata_default()
    {
        NoesisRuntime.Start();

        var sides = Sides("Nulls");
        foreach (var side in sides)
        {
            await Assert
                .That(float.IsNaN(side.Find<Border>("NullWidth").Width))
                .IsTrue()
                .Because(side.Name);
            await Assert
                .That(side.Find<Border>("NullOpacity").Opacity)
                .IsEqualTo(1f)
                .Because(side.Name);
            await Assert
                .That(side.Find<SpikeControl>("NullCount").Count)
                .IsEqualTo(3)
                .Because(side.Name);

            side.Model.MaybeCount = 40;
            side.Pump();
            await Assert
                .That(side.Find<Border>("NullWidth").Width)
                .IsEqualTo(40f)
                .Because(side.Name);
            await Assert
                .That(side.Find<SpikeControl>("NullCount").Count)
                .IsEqualTo(40)
                .Because(side.Name);

            side.Model.MaybeCount = null;
            side.Pump();
            await Assert
                .That(float.IsNaN(side.Find<Border>("NullWidth").Width))
                .IsTrue()
                .Because(side.Name);
            await Assert
                .That(side.Find<SpikeControl>("NullCount").Count)
                .IsEqualTo(3)
                .Because(side.Name);
        }

        await Assert
            .That(Bound(sides[0].Find<Border>("NullWidth"), FrameworkElement.WidthProperty))
            .IsFalse();
        await Assert
            .That(Bound(sides[0].Find<SpikeControl>("NullCount"), SpikeControl.CountProperty))
            .IsFalse();
    }

    [Test]
    public async Task A_converter_result_of_another_type_is_converted_as_the_native_engine_converts_it()
    {
        NoesisRuntime.Start();

        var sides = Sides("Results");
        foreach (var side in sides)
        {
            await Assert
                .That(side.Find<Border>("Visibility").Visibility)
                .IsEqualTo(Visibility.Collapsed)
                .Because(side.Name);

            side.Switch("OpacitySwitch").Result = 0.5f;
            side.Switch("BrushSwitch").Result = new SolidColorBrush(Color.FromArgb(255, 0, 0, 255));
            side.Switch("LabelSwitch").Result = "first";
            side.Switch("VisibilitySwitch").Result = Visibility.Hidden;
            side.Model.Tick = 1;
            side.Pump();
        }

        var compiled = sides[0];
        await Assert
            .That(Bound(compiled.Find<Border>("Opacity"), UIElement.OpacityProperty))
            .IsFalse();
        await Assert
            .That(Bound(compiled.Find<Border>("Brush"), Border.BackgroundProperty))
            .IsFalse();
        await Assert
            .That(Bound(compiled.Find<SpikeControl>("Label"), SpikeControl.LabelProperty))
            .IsFalse();
        await Assert
            .That(Bound(compiled.Find<Border>("Visibility"), UIElement.VisibilityProperty))
            .IsFalse();

        var steps = new (
            object? Opacity,
            object? Brush,
            object? Label,
            object? Visibility,
            float OpacityShown,
            string? BrushShown,
            string? LabelShown,
            Visibility VisibilityShown
        )[]
        {
            (
                0.25,
                "Red",
                new DateTime(2026, 1, 2),
                "Collapsed",
                0.25f,
                "#FFFF0000",
                "host-default",
                Visibility.Collapsed
            ),
            (2.5, 5, 5, true, 1f, null, "5", Visibility.Visible),
            (null, null, null, null, 1f, null, "host-default", Visibility.Visible),
            (-1.0, "#FF00FF00", 2.5, 2, 0f, "#FF00FF00", "2.5", Visibility.Visible),
        };

        foreach (
            var (
                opacity,
                brush,
                label,
                visibility,
                opacityShown,
                brushShown,
                labelShown,
                visibilityShown
            ) in steps
        )
        {
            foreach (var side in sides)
            {
                side.Switch("OpacitySwitch").Result = opacity;
                side.Switch("BrushSwitch").Result = brush;
                side.Switch("LabelSwitch").Result = label;
                side.Switch("VisibilitySwitch").Result = visibility;
                side.Model.Tick++;
                side.Pump();

                await Assert
                    .That(side.Find<Border>("Opacity").Opacity)
                    .IsEqualTo(opacityShown)
                    .Because(side.Name);
                await Assert
                    .That(
                        (side.Find<Border>("Brush").Background as SolidColorBrush)?.Color.ToString()
                    )
                    .IsEqualTo(brushShown)
                    .Because(side.Name);
                await Assert
                    .That(side.Find<SpikeControl>("Label").Label)
                    .IsEqualTo(labelShown)
                    .Because(side.Name);
                await Assert
                    .That(side.Find<Border>("Visibility").Visibility)
                    .IsEqualTo(visibilityShown)
                    .Because(side.Name);
            }
        }
    }

    [Test]
    public async Task A_converter_is_handed_the_registered_type_as_its_target()
    {
        NoesisRuntime.Start();

        var seen = new List<List<string>>();
        foreach (var side in Sides("Targets"))
            seen.Add(
                side.Recorder.Seen.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList()
            );

        await Assert
            .That(seen[1])
            .IsEquivalentTo([
                "Columns:System.UInt32",
                "Tag:Noesis.BaseComponent",
                "Width:System.Single",
            ]);
        await Assert.That(seen[0]).IsEquivalentTo(seen[1]);
    }

    [Test]
    public async Task A_slot_registered_as_uint_takes_and_gives_its_value()
    {
        NoesisRuntime.Start();

        var sides = Sides("Registered");
        foreach (var side in sides)
        {
            var pwd = side.Find<PasswordBox>("Pwd");
            await Assert.That(pwd.ShowLastCharacterDuration).IsEqualTo(1234).Because(side.Name);
            await Assert.That(pwd.PasswordChar).IsEqualTo('#').Because(side.Name);
            await Assert
                .That(side.Find<SpikeControl>("Counted").Count)
                .IsEqualTo(3)
                .Because(side.Name);
            await Assert
                .That(Panel.GetZIndex(side.Find<Border>("Layered")))
                .IsEqualTo(3)
                .Because(side.Name);
            await Assert
                .That(side.Find<TextBlock>("Masked").Text)
                .IsEqualTo("35")
                .Because(side.Name);
        }

        var compiled = sides[0];
        var compiledPwd = compiled.Find<PasswordBox>("Pwd");
        await Assert
            .That(Bound(compiledPwd, PasswordBox.ShowLastCharacterDurationProperty))
            .IsFalse();
        await Assert.That(Bound(compiledPwd, PasswordBox.PasswordCharProperty)).IsFalse();
        await Assert
            .That(Bound(compiled.Find<SpikeControl>("Counted"), SpikeControl.CountProperty))
            .IsFalse();
        await Assert.That(Bound(compiled.Find<Border>("Layered"), Panel.ZIndexProperty)).IsFalse();
        await Assert
            .That(Bound(compiled.Find<TextBlock>("Masked"), TextBlock.TextProperty))
            .IsFalse();
    }

    [Test]
    public async Task An_enum_slot_the_managed_side_never_registered_keeps_its_native_binding()
    {
        NoesisRuntime.Start();

        var sides = Sides("Registered");
        foreach (var side in sides)
        {
            var splitter = side.Find<GridSplitter>("Splitter");
            await Assert
                .That(splitter.ResizeDirection)
                .IsEqualTo(GridResizeDirection.Auto)
                .Because(side.Name);
            await Assert
                .That(Bound(splitter, GridSplitter.ResizeDirectionProperty))
                .IsTrue()
                .Because(side.Name);
        }
    }

    [Test]
    public async Task A_popup_closing_writes_back_to_an_element_property_as_the_native_binding_does()
    {
        NoesisRuntime.Start();

        foreach (var side in Sides("Popup"))
        {
            var popup = side.Find<Popup>("Pop");
            await Assert.That(popup.IsOpen).IsTrue().Because(side.Name);
            await Assert.That(Bound(popup, Popup.IsOpenProperty)).IsTrue().Because(side.Name);

            popup.IsOpen = false;
            side.Pump();
            await Assert
                .That(side.Find<Border>("Src").IsHitTestVisible)
                .IsFalse()
                .Because(side.Name);
        }
    }

    [Test]
    public async Task Every_slot_that_binds_two_way_by_default_refuses_a_path_it_cannot_write()
    {
        NoesisRuntime.Start();

        var compiled = Sides("TwoWay")[0];
        var slots = new (string Name, DependencyProperty Property)[]
        {
            ("IsDropDownOpen", ComboBox.IsDropDownOpenProperty),
            ("ExpandDirection", Expander.ExpandDirectionProperty),
            ("IsOpen", Popup.IsOpenProperty),
            ("ContextMenuIsOpen", ContextMenu.IsOpenProperty),
            ("ToolTipIsOpen", ToolTip.IsOpenProperty),
            ("IsChecked", MenuItem.IsCheckedProperty),
            ("IsSubmenuOpen", MenuItem.IsSubmenuOpenProperty),
            ("ListBoxItemIsSelected", ListBoxItem.IsSelectedProperty),
            ("TabItemIsSelected", TabItem.IsSelectedProperty),
            ("TreeViewItemIsSelected", TreeViewItem.IsSelectedProperty),
            ("IsOverflowOpen", ToolBar.IsOverflowOpenProperty),
            ("SliderSelectionStart", Slider.SelectionStartProperty),
            ("SliderSelectionEnd", Slider.SelectionEndProperty),
            ("TickBarSelectionStart", TickBar.SelectionStartProperty),
            ("TickBarSelectionEnd", TickBar.SelectionEndProperty),
            ("Value", Track.ValueProperty),
            ("SelectorIsSelected", Selector.IsSelectedProperty),
        };

        foreach (var (name, property) in slots)
        {
            var metadata =
                property.GetMetadata(compiled.Find<FrameworkElement>(name).GetType())
                as FrameworkPropertyMetadata;
            await Assert.That(metadata?.BindsTwoWayByDefault).IsTrue().Because(name);
            await Assert
                .That(Bound(compiled.Find<FrameworkElement>(name), property))
                .IsTrue()
                .Because(name);
        }
    }

    static bool Bound(DependencyObject element, DependencyProperty property) =>
        BindingOperations.GetBindingExpressionBase(element, property) is not null;

    // Compiled first, then parsed: each side is a document with its own converter instances.
    static Side[] Sides(string key) =>
        [
            new Side(
                "compiled",
                XamlGenerated.NoesisToolkitEquivalenceTests.FixturesCoSlotsxamlXaml.Build(),
                key
            ),
            new Side(
                "parsed",
                (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/CoSlots.xaml"),
                key
            ),
        ];

    sealed class Side
    {
        readonly ResourceDictionary _resources;
        readonly Grid _root = new Grid { Width = 400, Height = 2000 };
        readonly View _view;

        internal Side(string name, ResourceDictionary resources, string key)
        {
            Name = name;
            _resources = resources;
            _view = NoesisRuntime.Show(
                _root,
                new ContentControl
                {
                    Content = Model,
                    ContentTemplate = (DataTemplate)resources[key],
                }
            );
        }

        internal string Name { get; }

        internal CoModel Model { get; } = new CoModel();

        internal CoMultiSwitch MultiSwitch => (CoMultiSwitch)_resources["MultiSwitch"];

        internal CoRecorder Recorder => (CoRecorder)_resources["Recorder"];

        internal CoSwitch Switch(string key) => (CoSwitch)_resources[key];

        internal void Pump() => NoesisRuntime.Pump(_view, _root);

        internal T Find<T>(string name)
            where T : FrameworkElement =>
            TreeSearch.Named<T>(_root, name)
            ?? (T)TreeSearch.All<StackPanel>(_root).First().FindName(name);
    }
}
