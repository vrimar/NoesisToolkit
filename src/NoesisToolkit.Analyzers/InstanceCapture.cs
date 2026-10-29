using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace NoesisToolkit.Analyzers;

/// <summary>Whether a delegate has the containing instance as its target, which is what pins it.</summary>
static class InstanceCapture
{
    public static bool Captures(IOperation? handler)
    {
        while (handler is IConversionOperation conversion)
            handler = conversion.Operand;

        if (handler is not IDelegateCreationOperation creation)
            return false;

        return creation.Target switch
        {
            IMethodReferenceOperation method => TargetsInstance(method),
            IAnonymousFunctionOperation lambda => !lambda.Symbol.IsStatic
                && CapturesInBody(lambda.Body),
            _ => false,
        };
    }

    // A method group bound off another object holds that object, not this one.
    public static bool TargetsInstance(IMethodReferenceOperation method) =>
        method.Instance
            is IInstanceReferenceOperation
            {
                ReferenceKind: InstanceReferenceKind.ContainingTypeInstance
            };

    public static bool CapturesInBody(IOperation body) =>
        CapturesInBody(body, new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));

    static bool CapturesInBody(IOperation body, HashSet<IMethodSymbol> followed)
    {
        foreach (var op in body.Descendants())
        {
            if (
                op is IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance
                }
            )
                return true;

            if (
                LocalFunctionReached(op) is { IsStatic: false } local
                && followed.Add(local)
                && DeclarationOf(body, local) is { } declared
                && CapturesInBody(declared, followed)
            )
                return true;
        }

        return false;
    }

    static IMethodSymbol? LocalFunctionReached(IOperation op) =>
        op switch
        {
            IInvocationOperation { TargetMethod.MethodKind: MethodKind.LocalFunction } call =>
                call.TargetMethod.OriginalDefinition,
            IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction } reference =>
                reference.Method.OriginalDefinition,
            _ => null,
        };

    static IOperation? DeclarationOf(IOperation within, IMethodSymbol local)
    {
        var root = within;
        while (root.Parent is not null)
            root = root.Parent;

        foreach (var op in root.Descendants())
        {
            if (
                op is ILocalFunctionOperation declared
                && SymbolEqualityComparer.Default.Equals(declared.Symbol, local)
            )
                return declared;
        }

        return null;
    }
}
