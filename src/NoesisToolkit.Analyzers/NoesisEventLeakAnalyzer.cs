using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Analyzers;

/// <summary>
/// Requires every <c>element.Event += handler</c> inside a <c>Noesis.FrameworkElement</c>-derived type
/// to have a matching <c>-=</c>. Noesis holds handler delegates in a static table cleaned only on
/// native destruction, which the pinned managed proxy prevents, so an unbalanced subscription leaks
/// the element and its DataContext for the process lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoesisEventLeakAnalyzer : DiagnosticAnalyzer
{
    public const string UnbalancedNoesisEventId = "NTK2101";

    private const string FrameworkElementMetadataName = "Noesis.FrameworkElement";
    private const string DependencyObjectMetadataName = "Noesis.DependencyObject";

    private static readonly DiagnosticDescriptor UnbalancedNoesisEventRule = new(
        UnbalancedNoesisEventId,
        title: "Noesis element event subscription is never unsubscribed",
        messageFormat: "Noesis event '{0}' handler is never unsubscribed; add a matching '-=' before the element unloads (inline lambdas must become a named or field-stored handler)",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Noesis Managed keeps every 'element.Event += instanceHandler' delegate in a "
            + "static table cleaned only on native element destruction, which the pinned managed proxy "
            + "prevents. Any instance-method (or self-) subscription without a matching '-=', and any "
            + "inline lambda handler, pins the element and its DataContext for the process lifetime. "
            + "Static-method handlers are safe."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UnbalancedNoesisEventRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var frameworkElement = start.Compilation.GetTypeByMetadataName(
                FrameworkElementMetadataName
            );
            var dependencyObject = start.Compilation.GetTypeByMetadataName(
                DependencyObjectMetadataName
            );
            if (frameworkElement is null || dependencyObject is null)
                return;

            start.RegisterSymbolStartAction(
                symbolStart =>
                    AnalyzeNoesisEventBalance(symbolStart, frameworkElement, dependencyObject),
                SymbolKind.NamedType
            );
        });
    }

    private static void AnalyzeNoesisEventBalance(
        SymbolStartAnalysisContext context,
        INamedTypeSymbol frameworkElement,
        INamedTypeSymbol dependencyObject
    )
    {
        if (
            context.Symbol is not INamedTypeSymbol type
            || !IsOrInheritsFrom(type, frameworkElement)
        )
            return;

        var gate = new object();
        var subscribes =
            new List<(IEventSymbol Event, ISymbol? Handler, Location Location, bool AlwaysLeaks)>();
        var unsubscribes = new List<(IEventSymbol Event, ISymbol? Handler)>();

        context.RegisterOperationAction(
            opContext =>
            {
                var op = (IEventAssignmentOperation)opContext.Operation;
                if (op.EventReference is not IEventReferenceOperation reference)
                    return;

                var evt = reference.Event;

                // Only Noesis element events route through the leaking static handler store.
                if (
                    evt.ContainingType is null
                    || evt.ContainingType.ContainingAssembly?.Name.StartsWith(
                        "Noesis",
                        StringComparison.Ordinal
                    ) != true
                    || !IsOrInheritsFrom(evt.ContainingType, dependencyObject)
                )
                    return;

                var (skip, alwaysLeaks, handler) = ClassifyHandler(op.HandlerValue);
                if (skip)
                    return;

                lock (gate)
                {
                    if (op.Adds)
                        subscribes.Add((evt, handler, op.Syntax.GetLocation(), alwaysLeaks));
                    else
                        unsubscribes.Add((evt, handler));
                }
            },
            OperationKind.EventAssignment
        );

        context.RegisterSymbolEndAction(endContext =>
        {
            // Same event AND handler, so an unrelated one-shot `-=` cannot mask a permanent leak.
            var unsubUnknownHandler = new HashSet<IEventSymbol>(SymbolEqualityComparer.Default);
            var unsubHandlers = new Dictionary<IEventSymbol, HashSet<ISymbol>>(
                SymbolEqualityComparer.Default
            );
            foreach (var (evt, handler) in unsubscribes)
            {
                if (handler is null)
                    unsubUnknownHandler.Add(evt);
                else
                {
                    if (!unsubHandlers.TryGetValue(evt, out var set))
                        unsubHandlers[evt] = set = new HashSet<ISymbol>(
                            SymbolEqualityComparer.Default
                        );
                    set.Add(handler);
                }
            }

            foreach (var (evt, handler, location, alwaysLeaks) in subscribes)
            {
                // An unsubscribe nobody can name is evidence only for a subscribe nobody can name;
                // letting it vouch for a named handler silences every real leak on that event.
                var balanced =
                    !alwaysLeaks
                    && (
                        handler is null
                            ? unsubUnknownHandler.Contains(evt) || unsubHandlers.ContainsKey(evt)
                            : unsubHandlers.TryGetValue(evt, out var set) && set.Contains(handler)
                    );

                if (!balanced)
                {
                    endContext.ReportDiagnostic(
                        Diagnostic.Create(UnbalancedNoesisEventRule, location, evt.Name)
                    );
                }
            }
        });
    }

    // Static and non-capturing handlers cannot pin an instance; a capturing lambda cannot be removed.
    private static (bool Skip, bool AlwaysLeaks, ISymbol? Handler) ClassifyHandler(
        IOperation? handler
    )
    {
        while (handler is IConversionOperation conversion)
            handler = conversion.Operand;

        switch (handler)
        {
            case IDelegateCreationOperation creation:
                switch (creation.Target)
                {
                    case IMethodReferenceOperation method:
                        return (method.Method.IsStatic, false, method.Method);
                    case IAnonymousFunctionOperation lambda:
                        var capturesThis =
                            !lambda.Symbol.IsStatic && LambdaCapturesInstance(lambda.Body);
                        return (!capturesThis, capturesThis, null);
                    default:
                        return (false, false, null);
                }
            case IFieldReferenceOperation field:
                return (false, false, field.Field);
            case IPropertyReferenceOperation prop:
                return (false, false, prop.Property);
            case ILocalReferenceOperation local:
                return (false, false, local.Local);
            default:
                return (false, false, null);
        }
    }

    // Capturing `this` is the only capture that gives the delegate an instance target to pin.
    private static bool LambdaCapturesInstance(IOperation body)
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

    private static bool IsOrInheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType) =>
        TypeWalk.InheritsFrom(type, baseType);
}
