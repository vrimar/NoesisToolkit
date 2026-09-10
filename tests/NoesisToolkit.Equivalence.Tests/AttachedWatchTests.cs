using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class AttachedWatchTests
{
    [Test]
    public async Task An_attached_property_registered_natively_is_watched()
    {
        NoesisRuntime.Start();

        var target = new TextBlock();
        var seen = 0;
        DependencyWatcher.Watch(target, Grid.RowProperty, () => seen++);

        var grid = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(grid, target);

        // Wiring the probe is itself a change, so only what follows the baseline is the watch.
        var baseline = seen;

        Grid.SetRow(target, 3);
        NoesisRuntime.Pump(view, grid);

        await Assert.That(Grid.GetRow(target)).IsEqualTo(3);
        await Assert.That(seen).IsGreaterThan(baseline);
    }
}
