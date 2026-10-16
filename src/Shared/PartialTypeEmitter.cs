using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NoesisToolkit.CodeGen;

/// <summary>Reopening the type a generated member belongs to: the nesting chain a generator has to
/// re-declare, and the partial check that decides whether it may.</summary>
static class PartialTypeEmitter
{
    public static string TypeParameters(INamedTypeSymbol type) =>
        type.Arity == 0
            ? ""
            : "<" + string.Join(", ", type.TypeParameters.Select(p => p.Name)) + ">";

    /// <summary>One entry per nesting level, outermost first: a dotted join would give
    /// "class Outer.class Inner".</summary>
    public static string DeclarationChain(INamedTypeSymbol type)
    {
        var parts = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
            parts.Push($"{KindOf(current)} {current.Name}{TypeParameters(current)}");

        return string.Join(";", parts);
    }

    static string KindOf(INamedTypeSymbol type) =>
        type.TypeKind switch
        {
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => type.IsRecord ? "record" : "class",
        };

    /// <summary>Opens <paramref name="chain"/> as nested <c>partial</c> blocks, innermost last.
    /// Dispose in reverse to close them.</summary>
    public static List<CodeWriter.Scope> Open(CodeWriter w, string chain)
    {
        var levels = chain.Split(';');
        var scopes = new List<CodeWriter.Scope>();
        foreach (var level in levels)
            scopes.Add(w.Block($"partial {level}"));

        return scopes;
    }

    public static void Close(List<CodeWriter.Scope> scopes)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
            scopes[i].Dispose();
    }
}
