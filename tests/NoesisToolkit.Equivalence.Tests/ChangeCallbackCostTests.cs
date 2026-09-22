using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests;

public sealed partial class Probe : Control
{
    public static readonly List<DependencyPropertyChangedEventArgs> Seen = [];
    public static object? LastNewValue;

    [DependencyProperty(0)]
    public partial int Plain { get; set; }

    [DependencyProperty(0, nameof(OnReportedChanged))]
    public partial int Reported { get; set; }

    public static int Counted;

    // Reads nothing off the args: a NewValue read boxes, and that is Noesis' cost, not the callback's.
    [DependencyProperty(0, nameof(OnTickedChanged))]
    public partial int Ticked { get; set; }

    static void OnTickedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        Counted++;

    static void OnReportedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Seen.Add(e);
        LastNewValue = e.NewValue;
    }
}

[NotInParallel("Noesis")]
public sealed class ChangeCallbackCostTests
{
    const int Warmup = 8;
    const int Iterations = 64;

    static long Allocated(Action body)
    {
        for (var i = 0; i < Warmup; i++)
            body();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
            body();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Test]
    public async Task A_change_callback_reads_its_args_and_gets_the_same_object_each_time()
    {
        NoesisRuntime.Start();

        var probe = new Probe();
        NoesisRuntime.Show(probe);
        Probe.Seen.Clear();

        probe.Reported = 3;
        probe.Reported = 7;

        await Assert.That(Probe.Seen).HasCount().EqualTo(2);
        await Assert.That(Probe.LastNewValue).IsEqualTo(7);
        await Assert.That(Probe.Seen[1]).IsSameReferenceAs(Probe.Seen[0]);
        await Assert.That(Probe.Seen[0].NewValue).IsNull();
    }

    [Test]
    public async Task A_change_callback_allocates_nothing_beyond_the_write_itself()
    {
        NoesisRuntime.Start();

        var probe = new Probe();
        NoesisRuntime.Show(probe);

        var floor = Allocated(() => probe.Plain++);
        Probe.Counted = 0;
        var ticked = Allocated(() => probe.Ticked++);

        await Assert.That(floor).IsEqualTo(0);
        await Assert.That(ticked).IsEqualTo(0);
        await Assert.That(Probe.Counted).IsEqualTo(Warmup + Iterations);
    }
}
