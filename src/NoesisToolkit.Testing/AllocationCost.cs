using System;
using System.Diagnostics;
using System.Threading;

namespace NoesisToolkit.Testing;

/// <summary>
/// Measures the managed bytes a piece of work allocates on the calling thread once it is warm.
/// <para>
/// First sight costs what steady state does not — a JIT'd path, a proxy Noesis mints for an object it
/// has not met, a cache filling — so the body runs <c>warmup</c> times unmeasured before a window of
/// <c>iterations</c> measured runs. <see cref="Beyond"/> measures work against a floor that does
/// everything but the work under test (an idle <c>View.Update</c>, say), so anything a layout pass
/// allocates on its own cancels out.
/// </para>
/// <para>
/// A collection lets Noesis drop the proxies and weakly held objects the warmup paid for, and another
/// thread can hold shared state the body reads, so a single window can be charged what steady state is
/// not. Windows repeat a millisecond apart until one is clean or two agree, and the least is the cost:
/// a real one recurs in every window. A window a collection reached never counts toward agreement: each
/// re-mints the proxies its collection dropped, so two of them can agree on bytes steady state never
/// pays. <see cref="Once"/> measures a single cold run instead.
/// </para>
/// <para>
/// Work and floor alternate windows, so a disturbance that lasts reaches both, and each keeps its own
/// least: a disturbance only ever adds bytes. They agree only on a floor window no collection reached,
/// since that one re-mints the very proxies a regressed work would.
/// </para>
/// </summary>
public static class AllocationCost
{
    static readonly long Patience = Stopwatch.Frequency / 10;

    const int MinPairs = 4;

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

        var collections = GC.CollectionCount(0);
        var least = Window(body, iterations);
        var leastQuiet = Quiet(ref collections);
        var giveUpAt = Stopwatch.GetTimestamp() + Patience;
        while (least != 0 && Stopwatch.GetTimestamp() < giveUpAt)
        {
            Thread.Sleep(1);
            var next = Window(body, iterations);
            var quiet = Quiet(ref collections);
            if (next == least && quiet && leastQuiet)
                break;

            if (next < least || (next == least && quiet))
                (least, leastQuiet) = (next, quiet);
        }

        return least;
    }

    /// <summary>Bytes a window of <paramref name="work"/> allocates beyond a window of <paramref name="floor"/>, each measured as <see cref="Of"/> measures it, in alternating windows until their least agree.</summary>
    /// <param name="work">The work to measure; capture state outside it so the delegate is built once.</param>
    /// <param name="floor">Everything <paramref name="work"/> does but the work under test.</param>
    /// <param name="warmup">Unmeasured runs of each that pay first-sight costs.</param>
    /// <param name="iterations">Measured runs per window.</param>
    public static long Beyond(Action work, Action floor, int warmup = 8, int iterations = 64)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        if (floor is null)
            throw new ArgumentNullException(nameof(floor));

        if (warmup < 0)
            throw new ArgumentOutOfRangeException(nameof(warmup));

        if (iterations < 1)
            throw new ArgumentOutOfRangeException(nameof(iterations));

        for (var i = 0; i < warmup; i++)
        {
            floor();
            work();
        }

        var collections = GC.CollectionCount(0);
        return Paired(work, floor, body => (Window(body, iterations), Quiet(ref collections)));
    }

    /// <summary>Bytes the first run of <paramref name="work"/> after a full collection allocates beyond the first run of <paramref name="floor"/> after one: what rebuilding the proxies and weakly held objects a collection dropped costs the work.</summary>
    /// <param name="work">The work to measure; capture state outside it so the delegate is built once.</param>
    /// <param name="floor">Everything <paramref name="work"/> does but the work under test.</param>
    /// <param name="warmup">Runs of each before its collection, so the collection drops a warm state.</param>
    public static long BeyondAfterCollection(Action work, Action floor, int warmup = 2)
    {
        if (work is null)
            throw new ArgumentNullException(nameof(work));

        if (floor is null)
            throw new ArgumentNullException(nameof(floor));

        if (warmup < 0)
            throw new ArgumentOutOfRangeException(nameof(warmup));

        return Paired(
            work,
            floor,
            body =>
            {
                for (var i = 0; i < warmup; i++)
                    body();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var collections = GC.CollectionCount(0);
                return (Window(body, 1), Quiet(ref collections));
            }
        );
    }

    static long Paired(Action work, Action floor, Func<Action, (long Bytes, bool Quiet)> measure)
    {
        var leastWork = long.MaxValue;
        var leastFloor = long.MaxValue;
        var floorQuiet = false;
        var giveUpAt = Stopwatch.GetTimestamp() + Patience;
        for (var pair = 1; ; pair++)
        {
            var (floorBytes, quiet) = measure(floor);
            if (floorBytes < leastFloor)
                (leastFloor, floorQuiet) = (floorBytes, quiet);
            else if (floorBytes == leastFloor)
                floorQuiet |= quiet;

            leastWork = Math.Min(leastWork, measure(work).Bytes);
            if (leastWork == leastFloor && floorQuiet)
                return 0;

            if (pair >= MinPairs && Stopwatch.GetTimestamp() >= giveUpAt)
                return leastWork - leastFloor;

            Thread.Sleep(1);
        }
    }

    static bool Quiet(ref int collections)
    {
        var now = GC.CollectionCount(0);
        var quiet = now == collections;
        collections = now;
        return quiet;
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
