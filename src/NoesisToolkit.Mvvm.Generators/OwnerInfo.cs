using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NoesisToolkit.CodeGen;

/// <summary>The type a generated member is reopened into, as both MVVM generators need it.
/// <see cref="Key"/> names the file; <see cref="Fqn"/> groups and <see cref="Display"/> is what a
/// diagnostic prints, so the two never drift into each other's job.</summary>
/// <summary>Naming and reopening the type a generated member belongs to. The hint-name key and
/// the partial check are only ever asked by this generator, so they sit with it.</summary>
static class OwnerNaming
{
    /// <summary>
    /// A file-name-safe key for a type: its full name with generic arguments dropped and every
    /// separator flattened, so a nested or generic owner still gets one stable hint name.
    /// </summary>
    internal static string TypeKey(string fullyQualifiedName)
    {
        var builder = new System.Text.StringBuilder(fullyQualifiedName.Length);
        var depth = 0;
        foreach (var c in fullyQualifiedName.Replace("global::", ""))
        {
            if (c == '<')
                depth++;
            else if (c == '>')
                depth--;
            else if (depth == 0)
            {
                if (c == '.')
                    builder.Append('_');
                else if (c == '+')
                    builder.Append("__");
                else
                    builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>The hint-name key for a type, arity included so two generic arities do not collide.</summary>
    internal static string Key(INamedTypeSymbol type) =>
        TypeKey(type.Fq()) + (type.Arity == 0 ? "" : "`" + type.Arity);

    internal static bool IsPartial(ISymbol symbol) =>
        symbol
            .DeclaringSyntaxReferences.Select(r => r.GetSyntax())
            .OfType<MemberDeclarationSyntax>()
            .Any(d => d.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    /// <summary>The outermost type in <paramref name="type"/>'s chain that is not partial. Reopening
    /// a chain is only legal when every level of it is.</summary>
    internal static INamedTypeSymbol? NotPartialIn(INamedTypeSymbol type)
    {
        INamedTypeSymbol? offender = null;
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (!IsPartial(current))
                offender = current;
        }

        return offender;
    }
}

readonly record struct OwnerInfo(
    string Key,
    string Fqn,
    string Display,
    string Declarations,
    string? Namespace,
    string Constraints,
    bool IsPartial,
    string? NotPartial,
    LocationInfo? Location
)
{
    public static OwnerInfo From(INamedTypeSymbol type, string constraints = "") =>
        new OwnerInfo(
            OwnerNaming.Key(type),
            type.Fq(),
            type.ToDisplayString(),
            PartialTypeEmitter.DeclarationChain(type),
            type.NamespaceOrNull(),
            constraints,
            OwnerNaming.NotPartialIn(type) is null,
            OwnerNaming.NotPartialIn(type)?.ToDisplayString(),
            LocationInfo.From(type.Locations.FirstOrDefault())
        );
}
