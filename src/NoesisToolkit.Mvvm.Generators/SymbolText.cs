using Microsoft.CodeAnalysis;

namespace NoesisToolkit.CodeGen;

/// <summary>How a symbol is spelled in generated code. Only the generators emit type names,
/// so the formats sit with them.</summary>
static class SymbolText
{
    public static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat;

    // Keeps the `?` on nullable reference elements, which CS8619 would otherwise trip on.
    public static readonly SymbolDisplayFormat FqnNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    public static string Fq(this ISymbol symbol) => symbol.ToDisplayString(Fqn);
}
