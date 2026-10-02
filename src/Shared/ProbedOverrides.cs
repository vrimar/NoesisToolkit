using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace NoesisToolkit.CodeGen;

static class ProbedOverrides
{
    static readonly string[] Names =
    [
        "ToString",
        "Equals",
        "VisualChildrenCount",
        "GetVisualChild",
        "OnRender",
        "ConnectEvent",
        "ConnectField",
        "MeasureOverride",
        "ArrangeOverride",
        "OnPreApplyTemplate",
        "OnApplyTemplate",
        "OnPostApplyTemplate",
        "OnContentChanged",
        "GetContainerForItemOverride",
        "IsItemItsOwnContainerOverride",
        "GetDesiredTransform",
        "CloneCommonCore",
        "GetCurrentValueCore",
    ];

    static readonly HashSet<string> Probed = new HashSet<string>(Names, StringComparer.Ordinal);

    public static bool Named(string name) => Probed.Contains(name);

    public static IEnumerable<string> DeclaredBy(INamedTypeSymbol type)
    {
        foreach (var name in Names)
        {
            foreach (var member in type.GetMembers(name))
            {
                if (Signature(member) is { } signature)
                    yield return signature;
            }
        }
    }

    public static string? Signature(ISymbol member) =>
        member switch
        {
            IMethodSymbol method when Probed.Contains(method.Name) && ProbedByNoesis(method) =>
                DynamicDependencies.Signature(method),
            IPropertySymbol property
                when Probed.Contains(property.Name)
                    && property.GetMethod is { } getter
                    && ProbedByNoesis(getter) => property.Name,
            _ => null,
        };

    // Noesis probes ToString and Equals on every type it registers, view models included.
    static bool ProbedByNoesis(IMethodSymbol method)
    {
        if (!method.IsOverride)
            return false;

        var root = method;
        while (root.OverriddenMethod is { } overridden)
            root = overridden;

        return root.ContainingAssembly?.Name == "Noesis.GUI"
            || root.ContainingType?.SpecialType == SpecialType.System_Object;
    }
}
