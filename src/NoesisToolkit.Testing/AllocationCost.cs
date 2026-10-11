using System;
using System.Diagnostics;
using System.Threading;

namespace NoesisToolkit.Testing;

/// <summary>
/// Measures the managed bytes a piece of work allocates on the calling thread once it is warm.
/// <para>
/// First sight costs what steady state does not — a JIT'd path, a proxy Noesis mints for an object it
/// has not met, a cache filling — so the body runs <c>warmup</c> times unmeasured before a window of
/// <c>iterations</c> measured runs. Compare against the same measurement of a floor that does
/// everything but the work under test (an idle <c>View.Update</c>, say): the difference is the work's
/// own cost, and anything a layout pass allocates on its own cancels out.
/// </para>
/// <para>
/// A collection lets Noesis drop the proxies and weakly held objects the warmup paid for, and another
/// thread can hold shared state the body reads, so a single window can be charged what steady state is
/// not. Windows repeat a millisecond apart until one is clean or two agree, and the least is the cost:
/// a real one recurs in every window. <see cref="Once"/> measures a single cold run instead.
/// </para>
/// </summary>
public static class AllocationCost
{
    static readonly long Patience = Stopwatch.Frequency / 10;

    /// <summary>Bytes a window of <paramref name="iterations"/> runs of <paramref name="body"/> allocates, after <paramref name="warmup"/> unmeasured runs; the least of the windows measured.</summary>
    /// <param name="body">The work to measure; capture state outside it so the delegate is built once.</param>
    /// <param name="warmup">Unmeasured runs that pay first-sight costs.</param>
    /// <param name="iterations">Measured runs per window.</param>
    public static long Of(Action body, int warmup = 8, int iterations = 64)
    {
        if (body is null)
            throw new ArgumentNullException(nameof(body));

        if (warmup < 0)
            throw new ArgumentOutOfRangeException(nameof(warmup));

        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations));

        for (var i = 0; i < warmup; i++)
            body();

        var least = Window(body, iterations);
        var giveUpAt = Stopwatch.GetTimestamp() + Patience;
        while (least != 0 && Stopwatch.GetTimestamp() < giveUpAt)
        {
            Thread.Sleep(1);
            var next = Window(body, iterations);
            if (next == least)
                break;

            least = Math.Min(least, next);
        }

        return least;
    }

    /// <summary>Bytes one cold run of <paramref name="body"/> allocates, first-sight costs included: what the first run after a collection pays, say.</summary>
    /// <param name="body">The work to measure; capture state outside it so the delegate is built once.</param>
    public static long Once(Action body)
    {
        if (body is null)
            throw new ArgumentNullException(nameof(body));

        return Window(body, 1);
    }

    static long Window(Action body, int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
            body();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
