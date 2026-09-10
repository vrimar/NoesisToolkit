using System.Collections.Generic;
using System.Linq;

namespace NoesisToolkit.Xaml;

/// <summary>What became of each binding and each trigger in one document, so coverage is a build
/// artifact rather than something read back out of the generated C#.</summary>
sealed class BindingTally
{
    /// <summary>Emitted as a chain of typed reads.</summary>
    public int Compiled;

    /// <summary>Left to the native engine on purpose: a native source property feeding a native
    /// target property has no managed code in its path for a compiled binding to replace.</summary>
    public int NativeByDesign;

    /// <summary>Left to the native engine because the compiler could not resolve it. This is the
    /// only one of the three that measures a gap.</summary>
    public int Fallback;

    /// <summary>How many fell back for each reason, so the gap is answerable without instrumenting
    /// the emitter by hand every time someone asks what is left.</summary>
    public readonly Dictionary<string, int> Reasons = new Dictionary<string, int>();

    /// <summary>Triggers the compiler claimed, counted apart from bindings: a trigger that stays
    /// native takes its conditions with it, so folding the two hides which one gave way.</summary>
    public int TriggersCompiled;

    /// <summary>Triggers left to the native engine because the compiler could not claim them.</summary>
    public int TriggersFallback;

    /// <summary>How many triggers stayed native for each reason.</summary>
    public readonly Dictionary<string, int> TriggerReasons = new Dictionary<string, int>();

    /// <summary>A copy a speculative pass can be rolled back to.</summary>
    public BindingTally Snapshot()
    {
        var copy = new BindingTally
        {
            Compiled = Compiled,
            NativeByDesign = NativeByDesign,
            Fallback = Fallback,
            TriggersCompiled = TriggersCompiled,
            TriggersFallback = TriggersFallback,
        };

        foreach (var reason in Reasons)
            copy.Reasons[reason.Key] = reason.Value;

        foreach (var reason in TriggerReasons)
            copy.TriggerReasons[reason.Key] = reason.Value;

        return copy;
    }

    public void Restore(BindingTally snapshot)
    {
        Compiled = snapshot.Compiled;
        NativeByDesign = snapshot.NativeByDesign;
        Fallback = snapshot.Fallback;
        TriggersCompiled = snapshot.TriggersCompiled;
        TriggersFallback = snapshot.TriggersFallback;

        Reasons.Clear();
        foreach (var reason in snapshot.Reasons)
            Reasons[reason.Key] = reason.Value;

        TriggerReasons.Clear();
        foreach (var reason in snapshot.TriggerReasons)
            TriggerReasons[reason.Key] = reason.Value;
    }

    public void Fell(string reason)
    {
        Fallback++;
        Bump(Reasons, reason, 1);
    }

    public void TriggerFell(string reason)
    {
        TriggersFallback++;
        Bump(TriggerReasons, reason, 1);
    }

    public void Add(BindingTally other)
    {
        Compiled += other.Compiled;
        NativeByDesign += other.NativeByDesign;
        Fallback += other.Fallback;
        TriggersCompiled += other.TriggersCompiled;
        TriggersFallback += other.TriggersFallback;

        foreach (var pair in other.Reasons)
            Bump(Reasons, pair.Key, pair.Value);

        foreach (var pair in other.TriggerReasons)
            Bump(TriggerReasons, pair.Key, pair.Value);
    }

    static void Bump(Dictionary<string, int> into, string reason, int delta) =>
        into[reason] = into.TryGetValue(reason, out var count) ? count + delta : delta;

    public override string ToString()
    {
        var reachable = Compiled + Fallback;
        var share = reachable == 0 ? 100 : Compiled * 100 / reachable;
        return $"compiled {Compiled}   fallback {Fallback}   native by design {NativeByDesign}   "
            + $"coverage {share}%";
    }

    public string TriggerLine() =>
        $"triggers: compiled {TriggersCompiled}   fallback {TriggersFallback}";

    /// <summary>How many fell back for each kind of reason. Only the gap count is work: a boundary
    /// is a construct that must stay native, and an author row needs the document to change.</summary>
    public (int Gap, int Boundary, int Author) Split()
    {
        var gap = 0;
        var boundary = 0;
        var author = 0;
        foreach (var pair in Reasons)
            Count(pair.Key, pair.Value);

        foreach (var pair in TriggerReasons)
            Count(pair.Key, pair.Value);

        return (gap, boundary, author);

        void Count(string reason, int n)
        {
            switch (Refusals.Of(reason))
            {
                case RefusalKind.Boundary:
                    boundary += n;
                    break;
                case RefusalKind.Author:
                    author += n;
                    break;
                default:
                    gap += n;
                    break;
            }
        }
    }

    public IEnumerable<KeyValuePair<string, int>> Breakdown() => Ordered(Reasons);

    public IEnumerable<KeyValuePair<string, int>> TriggerBreakdown() => Ordered(TriggerReasons);

    static IEnumerable<KeyValuePair<string, int>> Ordered(Dictionary<string, int> reasons) =>
        reasons
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, System.StringComparer.Ordinal);
}
