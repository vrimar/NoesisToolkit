using System;
using System.Runtime;

namespace NoesisToolkit.Testing;

/// <summary>
/// Measures the managed bytes a piece of work allocates on the calling thread once it is warm.
/// <para>
/// First sight costs what steady state does not — a JIT'd path, a proxy Noesis mints for an object it
/// has not met, a cache filling — so the body runs <c>warmup</c> times unmeasured before
/// <c>iterations</c> measured runs. Compare against the same measurement of a floor that does
/// everything but the work under test (an idle <c>View.Update</c>, say): the difference is the work's
/// own cost, and anything a layout pass allocates on its own cancels out.
/// </para>
/// <para>
/// A collection lets Noesis drop the proxies and weakly held objects the warmup paid for, so one
/// between the runs — forced by another thread's garbage, say — charges first-sight costs again. The
/// warmup and the measured runs share a no-GC region when the runtime grants one.
/// </para>
/// </summary>
public static class AllocationCost
{
    /// <summary>The allocation budget of the no-GC region the runs share.</summary>
    public const long NoGcBudget = 64L * 1024 * 1024;

    /// <summary>Bytes allocated across <paramref name="iterations"/> runs of <paramref name="body"/>, after <paramref name="warmup"/> unmeasured runs.</summary>
    /// <param name="body">The work to measure; capture state outside it so the delegate is built once.</param>
    /// <param name="warmup">Unmeasured runs that pay first-sight costs.</param>
    /// <param name="iterations">Measured runs.</param>
    public static long Of(Action body, int warmup = 8, int iterations = 64)
    {
        if (body is null)
            throw new ArgumentNullException(nameof(body));

        if (warmup < 0)
            throw new ArgumentOutOfRangeException(nameof(warmup));

        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations));

        var noGc = TryStartNoGcRegion();
        try
        {
            for (var i = 0; i < warmup; i++)
                body();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < iterations; i++)
                body();

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally
        {
            if (noGc && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
                GC.EndNoGCRegion();
        }
    }

    // Refused inside an open region or over a budget the runtime cannot reserve.
    static bool TryStartNoGcRegion()
    {
        if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            return false;

        try
        {
            return GC.TryStartNoGCRegion(NoGcBudget);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
