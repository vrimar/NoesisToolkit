using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Analyzers;

/// <summary>
/// Requires every <c>element.Event += handler</c> or <c>element.AddHandler(routedEvent, handler)</c>
/// inside a <c>Noesis.FrameworkElement</c>-derived type to have a matching <c>-=</c> or
/// <c>RemoveHandler</c> that can run on a teardown path. Noesis holds handler delegates in a static
/// table cleaned only on native destruction, which the pinned managed proxy prevents, so an
/// unbalanced subscription leaks the element and its DataContext for the process lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoesisEventLeakAnalyzer : DiagnosticAnalyzer
{
    public const string UnbalancedNoesisEventId = "NTK2101";

    private const string FrameworkElementMetadataName = "Noesis.FrameworkElement";
    private const string DependencyObjectMetadataName = "Noesis.DependencyObject";
    private const string RoutedEventMetadataName = "Noesis.RoutedEvent";

    private const string MissingHint =
        "add a matching '-=' or RemoveHandler before the element unloads, or make the handler static";
    private const string GuardHint =
        "every '-=' for it is re-attached further down the same flow, which makes them re-entry "
        + "guards rather than teardowns; detach from Unloaded, or make the handler static";
    private const string LambdaHint =
        "a lambda capturing 'this' cannot be removed; give it a named or field-stored handler";

    private static readonly DiagnosticDescriptor UnbalancedNoesisEventRule = new(
        UnbalancedNoesisEventId,
        title: "Noesis element event subscription is never unsubscribed",
        messageFormat: "Noesis event '{0}' handler is never unsubscribed: {1}",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Noesis Managed keeps every 'element.Event += instanceHandler' and "
            + "'element.AddHandler(routedEvent, instanceHandler)' delegate in a static table cleaned only "
            + "on native element destruction, which the pinned managed proxy prevents. Any "
            + "instance-method (or self-) subscription without a matching '-=' or RemoveHandler, and any "
            + "inline lambda handler, pins the element and its DataContext for the process lifetime. "
            + "A '-=' that the same flow re-attaches does not count: it guards against stacking on a "
            + "second OnApplyTemplate, but never runs when the element goes away. Static-method "
            + "handlers are safe."
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

            var routedEvent = start.Compilation.GetTypeByMetadataName(RoutedEventMetadataName);

            start.RegisterSymbolStartAction(
                symbolStart =>
                    AnalyzeNoesisEventBalance(
                        symbolStart,
                        frameworkElement,
                        dependencyObject,
                        routedEvent
                    ),
                SymbolKind.NamedType
            );
        });
    }

    private static void AnalyzeNoesisEventBalance(
        SymbolStartAnalysisContext context,
        INamedTypeSymbol frameworkElement,
        INamedTypeSymbol dependencyObject,
        INamedTypeSymbol? routedEvent
    )
    {
        if (
            context.Symbol is not INamedTypeSymbol type
            || !IsOrInheritsFrom(type, frameworkElement)
        )
            return;

        var gate = new object();
        var subscribes = new List<Assignment>();
        var unsubscribes = new List<Assignment>();

        void Record(ISymbol evt, IOperation? handlerValue, bool adds, SyntaxNode syntax)
        {
            var (skip, unremovable, handler) = ClassifyHandler(handlerValue);
            if (skip)
                return;

            if (!adds && unremovable)
                return;

            lock (gate)
            {
                if (adds)
                    subscribes.Add(new Assignment(evt, handler, syntax, unremovable));
                else
                    unsubscribes.Add(new Assignment(evt, handler, syntax, false));
            }
        }

        context.RegisterOperationAction(
            opContext =>
            {
                var op = (IEventAssignmentOperation)opContext.Operation;
                if (
                    op.EventReference is IEventReferenceOperation reference
                    && (
                        IsNoesisElement(reference.Event.ContainingType, dependencyObject)
                        || IsRoutedWrapper(reference.Event, dependencyObject, routedEvent)
                    )
                )
                    Record(reference.Event, op.HandlerValue, op.Adds, op.Syntax);
            },
            OperationKind.EventAssignment
        );

        if (routedEvent is null)
            return;

        context.RegisterOperationAction(
            opContext =>
            {
                var call = (IInvocationOperation)opContext.Operation;
                var method = call.TargetMethod;
                var adds = method.Name == "AddHandler";
                if (
                    (adds || method.Name == "RemoveHandler")
                    && method.Parameters.Length == 2
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, routedEvent)
                    && IsNoesisElement(method.ContainingType, dependencyObject)
                    && RoutedEventOf(ArgumentFor(call, 0)) is { } evt
                )
                    Record(evt, ArgumentFor(call, 1), adds, call.Syntax);
            },
            OperationKind.Invocation
        );

        context.RegisterSymbolEndAction(endContext =>
        {
            // Same event AND handler, so an unrelated one-shot `-=` cannot mask a permanent leak.
            var unsubUnknownHandler = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var unsubHandlers = new Dictionary<ISymbol, HashSet<ISymbol>>(
                SymbolEqualityComparer.Default
            );
            foreach (var unsub in unsubscribes)
            {
                if (subscribes.Any(sub => Pairs(sub, unsub) && IsReEntryGuard(sub, unsub)))
                    continue;

                if (unsub.Handler is null)
                    unsubUnknownHandler.Add(unsub.Event);
                else
                {
                    if (!unsubHandlers.TryGetValue(unsub.Event, out var set))
                        unsubHandlers[unsub.Event] = set = new HashSet<ISymbol>(
                            SymbolEqualityComparer.Default
                        );
                    set.Add(unsub.Handler);
                }
            }

            foreach (var sub in subscribes)
            {
                // An unsubscribe nobody can name is evidence only for a subscribe nobody can name;
                // letting it vouch for a named handler silences every real leak on that event.
                var balanced =
                    !sub.Unremovable
                    && (
                        sub.Handler is null
                            ? unsubUnknownHandler.Contains(sub.Event)
                                || unsubHandlers.ContainsKey(sub.Event)
                            : unsubHandlers.TryGetValue(sub.Event, out var set)
                                && set.Contains(sub.Handler)
                    );

                if (balanced)
                    continue;

                var hint =
                    sub.Unremovable ? LambdaHint
                    : unsubscribes.Any(unsub => Pairs(sub, unsub)) ? GuardHint
                    : MissingHint;

                endContext.ReportDiagnostic(
                    Diagnostic.Create(
                        UnbalancedNoesisEventRule,
                        sub.Syntax.GetLocation(),
                        sub.Event.Name,
                        hint
                    )
                );
            }
        });
    }

    private readonly struct Assignment(
        ISymbol evt,
        ISymbol? handler,
        SyntaxNode syntax,
        bool unremovable
    )
    {
        public ISymbol Event { get; } = evt;
        public ISymbol? Handler { get; } = handler;
        public SyntaxNode Syntax { get; } = syntax;
        public bool Unremovable { get; } = unremovable;
    }

    private static bool Pairs(Assignment subscribe, Assignment unsubscribe) =>
        SymbolEqualityComparer.Default.Equals(subscribe.Event, unsubscribe.Event)
        && SymbolEqualityComparer.Default.Equals(subscribe.Handler, unsubscribe.Handler);

    private static bool IsReEntryGuard(Assignment subscribe, Assignment unsubscribe)
    {
        // Spans are per-tree, so two files of a partial type would compare as if they were one.
        if (unsubscribe.Syntax.SyntaxTree != subscribe.Syntax.SyntaxTree)
            return false;

        var common = unsubscribe
            .Syntax.Ancestors()
            .FirstOrDefault(node => node.Span.Contains(subscribe.Syntax.Span));

        if (!IsStatementList(common))
            return false;

        var removed = common.ChildThatContainsPosition(unsubscribe.Syntax.SpanStart);
        var added = common.ChildThatContainsPosition(subscribe.Syntax.SpanStart);
        return added.SpanStart > removed.SpanStart;
    }

    // Alternatives in an `if` are not one flow, so neither branch re-attaches the other.
    private static bool IsStatementList(SyntaxNode? node) =>
        node is BlockSyntax or SwitchSectionSyntax;

    // Static and non-capturing handlers cannot pin an instance; a capturing lambda cannot be removed.
    private static (bool Skip, bool Unremovable, ISymbol? Handler) ClassifyHandler(
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
                        return (!InstanceCapture.TargetsInstance(method), false, method.Method);
                    case IAnonymousFunctionOperation lambda:
                        var capturesThis =
                            !lambda.Symbol.IsStatic && InstanceCapture.CapturesInBody(lambda.Body);
                        return (!capturesThis, capturesThis, null);
                    default:
                        return (false, false, null);
                }
            case IFieldReferenceOperation field:
                return (field.Field.IsStatic && field.Field.IsReadOnly, false, field.Field);
            case IPropertyReferenceOperation prop:
                return (false, false, prop.Property);
            case ILocalReferenceOperation local:
                return (false, false, local.Local);
            default:
                return (false, false, null);
        }
    }

    // Only Noesis element events route through the leaking static handler store.
    private static bool IsNoesisElement(
        INamedTypeSymbol? type,
        INamedTypeSymbol dependencyObject
    ) =>
        type is not null
        && SymbolEqualityComparer.Default.Equals(
            type.ContainingAssembly,
            dependencyObject.ContainingAssembly
        )
        && IsOrInheritsFrom(type, dependencyObject);

    private static bool IsRoutedWrapper(
        IEventSymbol evt,
        INamedTypeSymbol dependencyObject,
        INamedTypeSymbol? routedEvent
    ) =>
        routedEvent is not null
        && IsOrInheritsFrom(evt.ContainingType, dependencyObject)
        && evt.ContainingType.GetMembers(evt.Name + "Event")
            .Any(member =>
                member.IsStatic
                && SymbolEqualityComparer.Default.Equals(
                    (member as IFieldSymbol)?.Type ?? (member as IPropertySymbol)?.Type,
                    routedEvent
                )
            );

    private static IOperation? ArgumentFor(IInvocationOperation call, int ordinal) =>
        call.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == ordinal)?.Value;

    // The CLR event a routed event backs is the same subscription, so either form balances the other.
    private static ISymbol? RoutedEventOf(IOperation? value)
    {
        while (value is IConversionOperation conversion)
            value = conversion.Operand;

        ISymbol? member = value switch
        {
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            _ => null,
        };
        if (member is null)
            return null;

        const string suffix = "Event";
        if (
            member.Name.Length > suffix.Length
            && member.Name.EndsWith(suffix, System.StringComparison.Ordinal)
            && member
                .ContainingType.GetMembers(
                    member.Name.Substring(0, member.Name.Length - suffix.Length)
                )
                .OfType<IEventSymbol>()
                .FirstOrDefault()
                is { } wrapper
        )
            return wrapper;

        return member;
    }

    private static bool IsOrInheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType) =>
        TypeWalk.InheritsFrom(type, baseType);
}
