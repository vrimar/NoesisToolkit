using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// The trailing fresh-control builds are the point: a bad metadata claim on a native property
// corrupts the type for the whole process, and only a later construction shows it.
[NotInParallel("Noesis")]
public sealed class CompiledReversibleTests
{
    [Test]
    public async Task An_enum_rides_selected_index_both_ways()
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
                Convert = v => v is SpikeStage t ? (object)(int)t : (object)default(int),
                Write = (o, v) =>
                    ((SpikeItem)o).Stage = v is int w ? (SpikeStage)w : default(SpikeStage),
                Mode = BindingMode.TwoWay,
            }
        );

        NoesisRuntime.Show(nativeBox, compiledBox);
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(1);
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(nativeBox.SelectedIndex);

        native.Stage = SpikeStage.Third;
        compiled.Stage = SpikeStage.Third;
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(2);
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(nativeBox.SelectedIndex);

        nativeBox.SelectedIndex = 0;
        compiledBox.SelectedIndex = 0;
        await Assert.That(compiled.Stage).IsEqualTo(SpikeStage.First);
        await Assert.That(compiled.Stage).IsEqualTo(native.Stage);

        NoesisRuntime.Show(new ComboBox());
    }

    [Test]
    public async Task A_double_rides_a_range_value_both_ways()
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
        CompiledBinding.Bind(
            compiledSlider,
            RangeBase.ValueProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Ratio],
                Convert = v => v is double t ? (object)(float)t : (object)default(float),
                Write = (o, v) => ((SpikeItem)o).Ratio = v is float w ? (double)w : default(double),
                Mode = BindingMode.TwoWay,
            }
        );

        NoesisRuntime.Show(nativeSlider, compiledSlider);
        await Assert.That(compiledSlider.Value).IsEqualTo(0.25f);
        await Assert.That(compiledSlider.Value).IsEqualTo(nativeSlider.Value);

        native.Ratio = 0.75;
        compiled.Ratio = 0.75;
        await Assert.That(compiledSlider.Value).IsEqualTo(0.75f);
        await Assert.That(compiledSlider.Value).IsEqualTo(nativeSlider.Value);

        nativeSlider.Value = 0.5f;
        compiledSlider.Value = 0.5f;
        await Assert.That(compiled.Ratio).IsEqualTo(0.5);
        await Assert.That(compiled.Ratio).IsEqualTo(native.Ratio);

        NoesisRuntime.Show(new Slider());
    }

    [Test]
    public async Task A_context_the_path_cannot_read_writes_the_default_as_the_native_binding_does()
    {
        NoesisRuntime.Start();

        var nativeBox = Box(null!);
        nativeBox.SetBinding(
            Selector.SelectedIndexProperty,
            new Binding(nameof(SpikeItem.Stage)) { Mode = BindingMode.TwoWay }
        );

        var compiledBox = Box(null!);
        CompiledBinding.Bind(
            compiledBox,
            Selector.SelectedIndexProperty,
            new CompiledBindingSpec
            {
                Hops = [BindingHop.Guarded<SpikeItem>(nameof(SpikeItem.Stage), s => s.Stage)],
                Convert = v => v is SpikeStage t ? (object)(int)t : (object)default(int),
                Write = (o, v) =>
                    ((SpikeItem)o).Stage = v is int w ? (SpikeStage)w : default(SpikeStage),
                Mode = BindingMode.TwoWay,
            }
        );

        NoesisRuntime.Show(nativeBox, compiledBox);
        nativeBox.SelectedIndex = 1;
        compiledBox.SelectedIndex = 1;

        // A guarded hop landing on the wrong type fails the path rather than reading null, and a
        // fresh root re-writes the default even though the path was already broken before.
        nativeBox.DataContext = "not an item";
        compiledBox.DataContext = "not an item";
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(-1);
        await Assert.That(compiledBox.SelectedIndex).IsEqualTo(nativeBox.SelectedIndex);
    }

    // Fails if the watch on a two-way target ever reports a write asynchronously.
    [Test]
    public async Task A_source_driven_update_does_not_push_the_slot_value_back()
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
        CompiledBinding.Bind(
            compiledSlider,
            RangeBase.ValueProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Ratio],
                Convert = v => v is double t ? (object)(float)t : (object)default(float),
                Write = (o, v) => ((SpikeItem)o).Ratio = v is float w ? (double)w : default(double),
                Mode = BindingMode.TwoWay,
            }
        );

        var view = NoesisRuntime.Show(nativeSlider, compiledSlider);

        // A double the float slot cannot hold exactly: a pushed-back value would not survive it.
        native.Ratio = 0.1;
        compiled.Ratio = 0.1;

        // The sliders under test, not a fresh view that reaches neither of them.
        NoesisRuntime.Pump(view, nativeSlider, compiledSlider);

        await Assert.That(compiled.Ratio).IsEqualTo(0.1);
        await Assert.That(compiled.Ratio).IsEqualTo(native.Ratio);
    }

    static ComboBox Box(object context)
    {
        var box = new ComboBox { DataContext = context };
        box.Items.Add("a");
        box.Items.Add("b");
        box.Items.Add("c");
        return box;
    }
}
