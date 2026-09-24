using System.Runtime;
using NoesisToolkit.Testing;

namespace NoesisToolkit.Testing.Tests;

public class AllocationCostTests
{
    sealed class Counter
    {
        public int Value;
    }

    [Test]
    public async Task Work_that_allocates_nothing_measures_zero()
    {
        var counter = new Counter();

        var cost = AllocationCost.Of(() => counter.Value++);

        await Assert.That(cost).IsEqualTo(0L);
    }

    [Test]
    public async Task Work_that_allocates_is_charged_for_every_measured_run()
    {
        var single = AllocationCost.Of(static () => GC.KeepAlive(new object()), iterations: 1);
        var many = AllocationCost.Of(static () => GC.KeepAlive(new object()), iterations: 32);

        await Assert.That(single).IsGreaterThan(0L);
        await Assert.That(many).IsEqualTo(single * 32);
    }

    [Test]
    public async Task A_first_sight_cost_falls_in_the_warmup()
    {
        List<int>? cache = null;

        var cost = AllocationCost.Of(() => cache ??= [], warmup: 1);

        await Assert.That(cost).IsEqualTo(0L);
    }

    [Test]
    public async Task Warmup_runs_are_not_measured_but_do_run()
    {
        var counter = new Counter();

        AllocationCost.Of(() => counter.Value++, warmup: 3, iterations: 5);

        await Assert.That(counter.Value).IsEqualTo(8);
    }

    [Test]
    public async Task The_runs_share_a_region_no_collection_interrupts()
    {
        var modes = new List<GCLatencyMode>();

        AllocationCost.Of(() => modes.Add(GCSettings.LatencyMode), warmup: 2, iterations: 2);

        await Assert.That(modes).All().Satisfy(mode => mode.IsEqualTo(GCLatencyMode.NoGCRegion));
        await Assert.That(GCSettings.LatencyMode).IsNotEqualTo(GCLatencyMode.NoGCRegion);
    }
}
