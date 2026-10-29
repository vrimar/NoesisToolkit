using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

readonly record struct MarkupDiagnostic(string Message, LocationInfo? Location);

/// <summary>What compiling one XAML document produced: the source to add, the diagnostics it
/// earned, and the counts the coverage survey aggregates. The registry and the per-file output
/// both read one of these, so the emitter runs once per document.</summary>
sealed class CompiledDocument(string file) : IEquatable<CompiledDocument>
{
    public string File { get; } = file;

    public string? HintName { get; set; }

    public string? Source { get; set; }

    /// <summary>True where an error must block <see cref="Source"/>. A loader partial is emitted
    /// either way, because nothing it contains depends on what the emitter could not resolve.</summary>
    public bool Gated { get; set; }

    public string? Crash { get; set; }

    public IReadOnlyList<string> Errors { get; set; } = [];

    public IReadOnlyList<LocationInfo?> ErrorSites { get; set; } = [];

    public string? RootClass { get; set; }

    public LocationInfo? ClassSite { get; set; }

    public IReadOnlyList<string> DeadMarkup { get; set; } = [];

    public IReadOnlyList<MarkupDiagnostic> Unrooted { get; set; } = [];

    public DiagnosticSeverity UnrootedSeverity { get; set; }

    public IReadOnlyList<MarkupDiagnostic> Absent { get; set; } = [];

    public IReadOnlyList<MarkupDiagnostic> LeftNative { get; set; } = [];

    public IReadOnlyList<MarkupDiagnostic> NativeCells { get; set; } = [];

    public BindingTally Tally { get; set; } = new BindingTally();

    public bool NeedsLoader { get; set; }

    public MarkupDiagnostic? Loader { get; set; }

    public IReadOnlyList<MarkupDiagnostic> UnresolvedTypes { get; set; } = [];

    /// <summary>The registry row a compiled dictionary contributes; null for every other outcome.</summary>
    public string? DictionaryRow { get; set; }

    public string? Logical { get; set; }

    public IReadOnlyList<(string Key, string? Leaf)> DeclaredKeys { get; set; } = [];

    public bool Equals(CompiledDocument? other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || File == other.File
                && HintName == other.HintName
                && Source == other.Source
                && Gated == other.Gated
                && Crash == other.Crash
                && Errors.SequenceEqual(other.Errors)
                && ErrorSites.SequenceEqual(other.ErrorSites)
                && RootClass == other.RootClass
                && Equals(ClassSite, other.ClassSite)
                && DeadMarkup.SequenceEqual(other.DeadMarkup)
                && Unrooted.SequenceEqual(other.Unrooted)
                && UnrootedSeverity == other.UnrootedSeverity
                && Absent.SequenceEqual(other.Absent)
                && LeftNative.SequenceEqual(other.LeftNative)
                && NativeCells.SequenceEqual(other.NativeCells)
                && SameTally(Tally, other.Tally)
                && NeedsLoader == other.NeedsLoader
                && Equals(Loader, other.Loader)
                && UnresolvedTypes.SequenceEqual(other.UnresolvedTypes)
                && DictionaryRow == other.DictionaryRow
                && Logical == other.Logical
                && DeclaredKeys.SequenceEqual(other.DeclaredKeys)
        );

    public override bool Equals(object? obj) => Equals(obj as CompiledDocument);

    public override int GetHashCode() =>
        (File.GetHashCode() * 397) ^ (Source?.GetHashCode() ?? 0) ^ Errors.Count;

    static bool SameTally(BindingTally a, BindingTally b) =>
        a.Compiled == b.Compiled
        && a.NativeByDesign == b.NativeByDesign
        && a.Fallback == b.Fallback
        && a.TriggersCompiled == b.TriggersCompiled
        && a.TriggersFallback == b.TriggersFallback
        && SameCounts(a.Reasons, b.Reasons)
        && SameCounts(a.TriggerReasons, b.TriggerReasons);

    static bool SameCounts(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count
        && a.All(pair => b.TryGetValue(pair.Key, out var count) && count == pair.Value);
}
