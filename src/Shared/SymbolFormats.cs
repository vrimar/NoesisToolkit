using Microsoft.CodeAnalysis;

namespace NoesisToolkit.CodeGen;

static class SymbolFormats
{
    /// <summary>Null for the global namespace, which contributes no segment to a qualified name.</summary>
    public static string? NamespaceOrNull(this ISymbol symbol) =>
        symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();
}
