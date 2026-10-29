using System;
using System.Collections.Generic;
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
    /// A file-name-safe key for a type: its namespace and every nesting level joined flat, each
    /// level's arity included so <c>Outer.Inner</c> and <c>Outer&lt;T&gt;.Inner</c> do not collide.
    /// </summary>
    internal static string Key(INamedTypeSymbol type)
    {
        var levels = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
            levels.Push(current.Arity == 0 ? current.Name : current.Name + "`" + current.Arity);

        for (
            var ns = type.ContainingNamespace;
            ns is { IsGlobalNamespace: false };
            ns = ns.ContainingNamespace
        )
            levels.Push(ns.Name);

        return string.Join("_", levels);
    }

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
    bool IsPartial,
    string? NotPartial,
    LocationInfo? Location
)
{
    public static OwnerInfo From(INamedTypeSymbol type)
    {
        var notPartial = OwnerNaming.NotPartialIn(type);
        return new OwnerInfo(
            OwnerNaming.Key(type),
            type.Fq(),
            type.ToDisplayString(),
            PartialTypeEmitter.DeclarationChain(type),
            type.NamespaceOrNull(),
            notPartial is null,
            notPartial?.ToDisplayString(),
            LocationInfo.From((notPartial ?? type).Locations.FirstOrDefault())
        );
    }
}
