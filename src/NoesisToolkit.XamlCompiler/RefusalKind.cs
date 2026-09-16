using System.Collections.Generic;

namespace NoesisToolkit.Xaml;

/// <summary>Why a construct stayed native, in the only distinction that decides what to do about
/// it.</summary>
enum RefusalKind
{
    /// <summary>The compiler could do this and does not yet. The only kind that measures a gap.</summary>
    Gap,

    /// <summary>Compiling this would change behaviour, so it must stay native however much effort
    /// is spent. Counting it as a gap invites a fix that would be a defect.</summary>
    Boundary,

    /// <summary>Nothing the compiler can do: the document has to change.</summary>
    Author,
}

static class Refusals
{
    static readonly Dictionary<string, RefusalKind> Kinds = new Dictionary<string, RefusalKind>
    {
        // A compiled write is a local value; these write below one, so winning is the defect.
        ["trigger-target-unnamed"] = RefusalKind.Boundary,
        ["trigger-setter-outranked"] = RefusalKind.Boundary,

        // Only a dependency property raises anything to watch.
        ["element-member-is-not-a-dependency-property"] = RefusalKind.Boundary,
        ["target-is-not-a-dependency-property"] = RefusalKind.Boundary,
        ["multi-binding-target-is-not-a-dependency-property"] = RefusalKind.Boundary,

        // A format and a one-way conversion cannot be run backwards.
        ["write-back-through-a-format"] = RefusalKind.Boundary,
        ["write-back-through-a-conversion"] = RefusalKind.Boundary,
        ["multi-binding-is-one-way-only"] = RefusalKind.Boundary,

        ["write-back-through-a-detached-receiver"] = RefusalKind.Boundary,

        // The document asked to write a path that has nowhere to write.
        ["write-back-has-no-setter"] = RefusalKind.Author,

        // A source read straight off a dependency property has no hop to write through, though
        // SetValue on that property would serve.
        ["write-back-through-a-source-property"] = RefusalKind.Gap,

        // SetValue dispatches on the declared type, so a near-enough value is a crash.
        ["value-does-not-fit-the-slot"] = RefusalKind.Boundary,

        // The write would feed the next read its own result.
        ["target-is-the-data-context"] = RefusalKind.Boundary,

        // A resource or binding value is a live lookup, which a constant cannot express.
        ["trigger-value-is-a-lookup"] = RefusalKind.Boundary,

        ["opted-out"] = RefusalKind.Author,
        ["multi-binding-opted-out"] = RefusalKind.Author,
        ["data-context-undeclared"] = RefusalKind.Author,
        ["hop-missing"] = RefusalKind.Author,
        ["element-has-no-such-member"] = RefusalKind.Author,
    };

    internal static RefusalKind Of(string reason) =>
        Kinds.TryGetValue(reason, out var kind) ? kind : RefusalKind.Gap;
}
