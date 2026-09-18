using System.Collections.Generic;

namespace NoesisToolkit.Testing;

/// <summary>What one <see cref="ControlCollectability.Probe"/> sweep found.</summary>
public sealed class CollectabilityReport
{
    internal CollectabilityReport(
        IReadOnlyList<string> leaked,
        IReadOnlyList<string> skipped,
        int verified
    )
    {
        Leaked = leaked;
        Skipped = skipped;
        Verified = verified;
    }

    /// <summary>Full names of the controls the heap still held after removal and collection.</summary>
    public IReadOnlyList<string> Leaked { get; }

    /// <summary>
    /// Controls that could not be constructed headless, each with the exception type that stopped it.
    /// Assert on <see cref="Verified"/> as well: a sweep that skips everything reports no leaks.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; }

    /// <summary>How many controls were exercised and found collectable.</summary>
    public int Verified { get; }
}
