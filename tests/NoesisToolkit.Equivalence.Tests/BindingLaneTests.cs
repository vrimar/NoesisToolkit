using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// The typed lane, judged against the native binding it replaces and against the boxed route's
// allocations, which it exists to remove.
[NotInParallel("Noesis")]
public sealed class BindingLaneTests
{
    static CompiledBindingSpec RatioLane(bool guarded = false) =>
        new()
        {
            Hops = [Hop.Ratio],
            Lane = BindingLane.Of<SpikeItem, double, float>(
                static o => o.Ratio,
                static t => (float)t,
                static (o, w) => o.Ratio = w,
                guarded
            ),
            Mode = BindingMode.TwoWay,
        };

    [Test]
    public async Task A_double_rides_a_range_value_both_ways_as_the_native_binding_does()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Ratio = 0.25 };
        var compiled = new SpikeItem { Ratio = 0.25 };

        var nativeSlider = new Slider { Maximum = 1, DataContext = native };
        nativeSlider.SetBinding(
            RangeBase.ValueProperty,
            new Binding(nameof(SpikeItem.Ratio)) { Mode = BindingMode.TwoWay }
        );

        var compiledSlider = new Slider { Maximum = 1, DataContext = compiled };
        CompiledBinding.Bind(compiledSlider, RangeBase.ValueProperty, RatioLane());

        NoesisRuntime.Show(nativeSlider, compiledSlider);
        await Assert.That(compiledSlider.Value).IsEqualTo(0.25f);
        await Assert.That(compiledSlider.Value).IsEqualTo(nativeSlider.Value);

        native.Ratio = 0.75;
        compiled.Ratio = 0.75;
        await Assert.That(compiledSlider.Value).IsEqualTo(nativeSlider.Value);

        native.Ratio = 4;
        compiled.Ratio = 4;
        await Assert.That(compiledSlider.Value).IsEqualTo(nativeSlider.Value);
        await Assert.That(compiled.Ratio).IsEqualTo(native.Ratio);

        nativeSlider.Value = 0.5f;
        compiledSlider.Value = 0.5f;
        await Assert.That(compiled.Ratio).IsEqualTo(0.5);
        await Assert.That(compiled.Ratio).IsEqualTo(native.Ratio);
    }

    [Test]
    public async Task An_enum_rides_selected_index_both_ways_as_the_native_binding_does()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Stage = SpikeStage.Second };
        var compiled = new SpikeItem { Stage = SpikeStage.Second };

        var nativeBox = Box(native);
        nativeBox.SetBinding(
            Selector.SelectedIndexProperty,
            new Binding(nameof(SpikeItem.Stage)) { Mode = BindingMode.TwoWay }
        );

        var compiledBox = Box(compiled);
        CompiledBinding.Bind(
            compiledBox,
            Selector.SelectedIndexProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Stage],
                Lane = BindingLane.Of<SpikeItem, SpikeStage, int>(
                    static o => o.Stage,
                    static t => (int)t,
                    static (o, w) => o.Stage = (SpikeStage)w,
                    false
                ),
                Mode = BindingMode.TwoWay,
            }
        );

        NoesisRuntime.Show(nativeBox, compiledBox);
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(1);
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(nativeBox.SelectedIndex);

        native.Stage = SpikeStage.Third;
        compiled.Stage = SpikeStage.Third;
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(nativeBox.SelectedIndex);

        nativeBox.SelectedIndex = 0;
        compiledBox.SelectedIndex = 0;
        await Assert.That(compiled.Stage).IsEqualTo(SpikeStage.First);
        await Assert.That(compiled.Stage).IsEqualTo(native.Stage);
    }

    [Test]
    public async Task A_guarded_lane_landing_on_the_wrong_type_writes_the_default()
    {
        NoesisRuntime.Start();

        var nativeSlider = new Slider
        {
            Maximum = 1,
            DataContext = new SpikeItem { Ratio = 0.5 },
        };
        nativeSlider.SetBinding(
            RangeBase.ValueProperty,
            new Binding(nameof(SpikeItem.Ratio)) { Mode = BindingMode.TwoWay }
        );

        var compiledSlider = new Slider
        {
            Maximum = 1,
            DataContext = new SpikeItem { Ratio = 0.5 },
        };
        CompiledBinding.Bind(compiledSlider, RangeBase.ValueProperty, RatioLane(guarded: true));

        NoesisRuntime.Show(nativeSlider, compiledSlider);
        await Assert.That(compiledSlider.Value).IsEqualTo(0.5f);

        nativeSlider.DataContext = "not an item";
        compiledSlider.DataContext = "not an item";
        await Assert.That(compiledSlider.Value).IsEqualTo(nativeSlider.Value);
    }

    // A slider dragged through values it has never shown: the boxed route keeps a box per value.
    [Test]
    public async Task A_lane_moves_through_fresh_values_both_ways_without_allocating()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem { Ratio = 0 };
        var slider = new Slider { Maximum = 100000, DataContext = item };
        CompiledBinding.Bind(slider, RangeBase.ValueProperty, RatioLane());
        NoesisRuntime.Show(slider);

        item.Ratio = 1;
        slider.Value = 2;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 3; i < 515; i += 2)
        {
            item.Ratio = i;
            slider.Value = i + 1;
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(item.Ratio).IsEqualTo(514.0);
        await Assert.That(allocated).IsEqualTo(0L);
    }

    static ComboBox Box(object context)
    {
        var box = new ComboBox { DataContext = context };
        box.Items.Add("a");
        box.Items.Add("b");
        box.Items.Add("c");
        return box;
    }

    static CompiledBindingSpec IndexText() =>
        new()
        {
            Hops = [Hop.Index],
            Lane = BindingLane.Text<SpikeItem, int>(
                static o => o.Index,
                static (int t, Span<char> d, out int w) =>
                {
                    var s = new SlotText(d);
                    s.Literal("#");
                    s.Integer(t, "N0");
                    return s.Done(out w);
                },
                false
            ),
        };

    static CompiledBindingSpec RatioText() =>
        new()
        {
            Hops = [Hop.Ratio],
            Lane = BindingLane.Text<SpikeItem, double>(
                static o => o.Ratio,
                static (double t, Span<char> d, out int w) =>
                {
                    var s = new SlotText(d);
                    s.Fixed(t, "F2");
                    return s.Done(out w);
                },
                false
            ),
        };

    [Test]
    [Arguments(0)]
    [Arguments(7)]
    [Arguments(1234567)]
    [Arguments(-42)]
    public async Task A_number_rides_a_text_slot_as_the_native_format_writes_it(int index)
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Index = index };
        var compiled = new SpikeItem { Index = index };

        var nativeText = new TextBlock { DataContext = native };
        nativeText.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(SpikeItem.Index)) { StringFormat = "#{0:N0}" }
        );

        var compiledText = new TextBlock { DataContext = compiled };
        CompiledBinding.Bind(compiledText, TextBlock.TextProperty, IndexText());

        NoesisRuntime.Show(nativeText, compiledText);
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);

        native.Index = index + 1;
        compiled.Index = index + 1;
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
    }

    [Test]
    [Arguments(2.675)]
    [Arguments(-0.004)]
    [Arguments(1e21)]
    [Arguments(double.PositiveInfinity)]
    public async Task A_fixed_format_rounds_in_a_text_lane_as_the_engine_rounds(double ratio)
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Ratio = ratio };
        var compiled = new SpikeItem { Ratio = ratio };

        var nativeText = new TextBlock { DataContext = native };
        nativeText.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(SpikeItem.Ratio)) { StringFormat = "{0:F2}" }
        );

        var compiledText = new TextBlock { DataContext = compiled };
        CompiledBinding.Bind(compiledText, TextBlock.TextProperty, RatioText());

        NoesisRuntime.Show(nativeText, compiledText);
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
    }

    [Test]
    public async Task A_text_lane_shows_values_it_never_showed_before_without_allocating()
    {
        NoesisRuntime.Start();

        var index = new SpikeItem();
        var ratio = new SpikeItem();
        var indexText = new TextBlock { DataContext = index };
        var ratioText = new TextBlock { DataContext = ratio };
        CompiledBinding.Bind(indexText, TextBlock.TextProperty, IndexText());
        CompiledBinding.Bind(ratioText, TextBlock.TextProperty, RatioText());
        NoesisRuntime.Show(indexText, ratioText);

        for (var i = 0; i < 8; i++)
        {
            index.Index += 1001;
            ratio.Ratio += 0.37;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 300; i++)
        {
            index.Index += 1001;
            ratio.Ratio += 0.37;
        }

        await Assert.That(GC.GetAllocatedBytesForCurrentThread() - before).IsEqualTo(0);
        await Assert
            .That(indexText.Text)
            .IsEqualTo(
                "#" + index.Index.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            );
    }
}
