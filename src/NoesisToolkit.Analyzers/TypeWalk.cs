using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NoesisToolkit.CodeGen;

/// <summary>Walking a type's base chain, which both analyzers do for their own reasons.</summary>
static class TypeWalk
{
    public static bool InheritsFrom(ITypeSymbol type, ITypeSymbol target)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target))
                return true;
        }

        return false;
    }

    /// <summary>The nearest property or field named <paramref name="name"/>, base chain first.</summary>
    public static ISymbol? FindMember(
        ITypeSymbol type,
        string name,
        bool staticOnly = false,
        bool interfaces = false
    )
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var member = current
                .GetMembers(name)
                .FirstOrDefault(m =>
                    (!staticOnly || m.IsStatic) && m is IPropertySymbol or IFieldSymbol
                );

            if (member is not null)
                return member;
        }

        return interfaces
            ? type
                .AllInterfaces.SelectMany(i => i.GetMembers(name))
                .FirstOrDefault(m => m is IPropertySymbol or IFieldSymbol)
            : null;
    }
}
