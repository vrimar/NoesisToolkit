using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledBindingMissTests
{
    static CompiledBindingSpec LabelSpec(BindingHop hop) => new() { Hops = [hop] };

    static async Task<List<string>> Recording(Func<Task> act)
    {
        var misses = new List<string>();
        Action<string> record = misses.Add;
        CompiledBindingDiagnostics.Missed += record;
        try
        {
            await act();
        }
        finally
        {
            CompiledBindingDiagnostics.Missed -= record;
        }

        return misses;
    }

    [Test]
    public async Task A_data_context_of_the_wrong_type_writes_the_default_and_reports_a_miss()
    {
        NoesisRuntime.Start();

        var native = new TextBlock { DataContext = new SpikeItem { Label = "kept" } };
        native.SetBinding(TextBlock.TextProperty, new Binding(nameof(SpikeItem.Label)));

        var compiled = new TextBlock { DataContext = new SpikeItem { Label = "kept" } };
        CompiledBinding.Bind(
            compiled,
            TextBlock.TextProperty,
            LabelSpec(BindingHop.Checked<SpikeItem>(nameof(SpikeItem.Label), static s => s.Label))
        );

        NoesisRuntime.Show(native, compiled);
        await Assert.That(compiled.Text).IsEqualTo("kept");

        var misses = await Recording(() =>
        {
            native.DataContext = "not an item";
            compiled.DataContext = "not an item";
            return Task.CompletedTask;
        });

        await Assert.That(compiled.Text).IsEqualTo(native.Text);
        await Assert.That(misses).IsNotEmpty();
        await Assert.That(misses[0]).Contains("System.String");
        await Assert.That(misses[0]).Contains(nameof(SpikeItem.Label));
    }

    [Test]
    public async Task A_lane_off_a_data_context_of_the_wrong_type_reports_a_miss()
    {
        NoesisRuntime.Start();

        var slider = new Slider
        {
            Maximum = 1,
            DataContext = new SpikeItem { Ratio = 0.5 },
        };
        CompiledBinding.Bind(
            slider,
            RangeBase.ValueProperty,
            new CompiledBindingSpec
            {
                Hops =
                [
                    BindingHop.CheckedValue<SpikeItem, double>(
                        nameof(SpikeItem.Ratio),
                        static o => o.Ratio
                    ),
                ],
                Lane = BindingLane.Of<SpikeItem, double, float>(
                    static o => o.Ratio,
                    static t => (float)t,
                    null,
                    guarded: true
                ),
            }
        );
        NoesisRuntime.Show(slider);

        var misses = await Recording(() =>
        {
            slider.DataContext = "not an item";
            return Task.CompletedTask;
        });

        await Assert.That(misses).IsNotEmpty();
        await Assert.That(misses[0]).Contains(nameof(SpikeItem.Ratio));
    }

    [Test]
    public async Task A_guarded_hop_misses_quietly()
    {
        NoesisRuntime.Start();

        var compiled = new TextBlock { DataContext = new SpikeItem { Label = "kept" } };
        CompiledBinding.Bind(
            compiled,
            TextBlock.TextProperty,
            LabelSpec(BindingHop.Guarded<SpikeItem>(nameof(SpikeItem.Label), static s => s.Label))
        );
        NoesisRuntime.Show(compiled);

        var misses = await Recording(() =>
        {
            compiled.DataContext = "not an item";
            return Task.CompletedTask;
        });

        await Assert.That(misses).IsEmpty();
    }
}
