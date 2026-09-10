using System.Globalization;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// What a native MultiBinding does with a broken child and with StringFormat, pinned because
// CompiledMultiBinding has to land in the same place.
[NotInParallel("Noesis")]
public sealed class MultiBindingSpikeTests
{
    [Test]
    public async Task A_converter_sees_each_child_value_and_a_broken_child_arrives_marked()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem { Label = "left", Index = 4 };
        var probe = new ProbeJoin("+");

        var multi = new MultiBinding { Converter = probe };
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Label)));
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Index)));
        multi.Bindings.Add(new Binding("Payload.Index"));

        var target = new TextBlock { DataContext = item };
        BindingOperations.SetBinding(target, TextBlock.TextProperty, multi);

        NoesisRuntime.Show(target);

        // A child whose path ran out arrives marked, not as null, and the others still resolve.
        await Assert.That(probe.Seen.All(s => s == "String:left|Int32:4|<unset>")).IsTrue();
        await Assert.That(target.Text).IsEqualTo("String:left+Int32:4+<unset>");

        probe.Seen.Clear();
        item.Index = 9;
        await Assert.That(probe.Seen).Contains("String:left|Int32:9|<unset>");
        await Assert.That(target.Text).IsEqualTo("String:left+Int32:9+<unset>");

        probe.Seen.Clear();
        item.Payload = new SpikeItem { Index = 77 };
        await Assert.That(probe.Seen).Contains("String:left|Int32:9|Int32:77");
        await Assert.That(target.Text).IsEqualTo("String:left+Int32:9+Int32:77");
    }

    [Test]
    public async Task A_converter_returning_unset_leaves_the_slot_at_its_default()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem { Flag = true };

        var multi = new MultiBinding { Converter = new UnsetWhenTrue() };
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Flag)));

        var target = new SpikeControl { DataContext = item };
        BindingOperations.SetBinding(target, SpikeControl.LabelProperty, multi);

        NoesisRuntime.Show(target);
        await Assert.That(target.Label).IsEqualTo("host-default");

        item.Flag = false;
        await Assert.That(target.Label).IsEqualTo("settled");
    }

    sealed class UnsetWhenTrue : IMultiValueConverter
    {
        public object? Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        ) => values[0] is true ? DependencyProperty.UnsetValue : "settled";

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotSupportedException();
    }
}
