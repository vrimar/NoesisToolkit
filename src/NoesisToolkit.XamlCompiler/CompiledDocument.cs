using System.Collections.Generic;

namespace NoesisToolkit.Xaml;

/// <summary>What compiling one XAML document produced: the source to add, the diagnostics it
/// earned, and the counts the coverage survey aggregates. The registry and the per-file output
/// both read one of these, so the emitter runs once per document.</summary>
sealed class CompiledDocument(string file)
{
    public string File { get; } = file;

    public string? HintName { get; set; }

    public string? Source { get; set; }

    /// <summary>True where an error must block <see cref="Source"/>. A loader partial is emitted
    /// either way, because nothing it contains depends on what the emitter could not resolve.</summary>
    public bool Gated { get; set; }

    public string? Crash { get; set; }

    public IReadOnlyList<string> Errors { get; set; } = [];

    public IReadOnlyList<string> DeadMarkup { get; set; } = [];

    public BindingTally Tally { get; set; } = new BindingTally();

    public bool NeedsLoader { get; set; }

    /// <summary>The registry row a compiled dictionary contributes; null for every other outcome.</summary>
    public string? DictionaryRow { get; set; }

    public string? Logical { get; set; }

    public IReadOnlyList<(string Key, string? Leaf)> DeclaredKeys { get; set; } = [];
}
