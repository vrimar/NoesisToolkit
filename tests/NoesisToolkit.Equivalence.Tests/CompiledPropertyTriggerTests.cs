using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledPropertyTriggerTests
{
    static SpikeControl Templated()
    {
        var compiled =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesTemplatedChromexamlXaml.Build();

        return new SpikeControl
        {
            Template = (ControlTemplate)compiled["Triggered"],
            Label = "caption",
        };
    }

    static Border Chrome(SpikeControl host) => TreeSearch.All<Border>(host).First();

    static TextBlock Caption(SpikeControl host) => TreeSearch.All<TextBlock>(host).First();

    [Test]
    public async Task A_property_trigger_in_a_template_compiles()
    {
        var host = Templated();
        NoesisRuntime.Show(host);

        await Assert.That(CompiledTriggerSet.SetsOf(Chrome(host))).IsNotEmpty();
    }

    [Test]
    public async Task A_property_trigger_applies_and_reverts_off_the_templated_parent()
    {
        var host = Templated();
        var view = NoesisRuntime.Show(host);

        await Assert.That(Caption(host).Opacity).IsEqualTo(1f);

        host.Align = VerticalAlignment.Top;
        NoesisRuntime.Pump(view, host);
        await Assert.That(Caption(host).Opacity).IsEqualTo(0.5f);
        await Assert.That(((SolidColorBrush)Chrome(host).Background).Color).IsEqualTo(Colors.Green);

        host.Align = VerticalAlignment.Center;
        NoesisRuntime.Pump(view, host);
        await Assert.That(Caption(host).Opacity).IsEqualTo(1f);
        await Assert.That(((SolidColorBrush)Chrome(host).Background).Color).IsEqualTo(Colors.Red);
    }

    [Test]
    public async Task A_self_condition_reads_the_templated_parent()
    {
        var host = Templated();
        var view = NoesisRuntime.Show(host);

        await Assert.That(Caption(host).FontStyle).IsEqualTo(FontStyle.Normal);

        host.Label = "flagged";
        NoesisRuntime.Pump(view, host);
        await Assert.That(Caption(host).FontStyle).IsEqualTo(FontStyle.Italic);
    }

    [Test]
    public async Task Every_condition_of_a_compiled_multi_trigger_must_hold()
    {
        var host = Templated();
        var view = NoesisRuntime.Show(host);

        host.Align = VerticalAlignment.Bottom;
        NoesisRuntime.Pump(view, host);
        await Assert.That(Caption(host).Opacity).IsEqualTo(1f);

        host.Count = 2;
        NoesisRuntime.Pump(view, host);
        await Assert.That(Caption(host).Opacity).IsEqualTo(0.25f);

        host.Count = 3;
        NoesisRuntime.Pump(view, host);
        await Assert.That(Caption(host).Opacity).IsEqualTo(1f);
    }
}
