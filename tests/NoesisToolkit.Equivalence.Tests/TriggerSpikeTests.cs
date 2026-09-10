using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class TriggerSpikeTests
{
    static Style BuildStyle()
    {
        var style = new Style { TargetType = typeof(SpikeControl) };
        style.Setters.Add(new Setter { Property = SpikeControl.LabelProperty, Value = "base" });

        var first = new DataTrigger { Binding = new Binding("Flag"), Value = "True" };
        first.Setters.Add(new Setter { Property = SpikeControl.LabelProperty, Value = "first" });
        style.Triggers.Add(first);

        var second = new DataTrigger { Binding = new Binding("Index"), Value = "1" };
        second.Setters.Add(new Setter { Property = SpikeControl.LabelProperty, Value = "second" });
        style.Triggers.Add(second);

        return style;
    }

    [Test]
    public async Task A_local_value_beats_an_active_trigger_setter()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem { Flag = true };
        var host = new SpikeControl
        {
            DataContext = item,
            Style = BuildStyle(),
            Label = "local",
        };

        NoesisRuntime.Show(host);
        await Assert.That(host.Label).IsEqualTo("local");
    }
}
