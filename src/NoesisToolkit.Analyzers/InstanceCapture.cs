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

    public static bool CapturesInBody(IOperation body)
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
        }

        return false;
    }
}
