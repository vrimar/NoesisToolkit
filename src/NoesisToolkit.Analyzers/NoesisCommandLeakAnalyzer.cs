using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Analyzers;

/// <summary>
/// Refuses a command on a <c>Noesis.FrameworkElement</c>-derived type whose delegates close over the
/// element. Bound into that element's own template, the native <c>Command</c> property holds the
/// managed command, the command holds the element, and the element owns the template child — a cycle
/// across the native boundary that neither the collector nor the refcount can break. It covers a
/// command the element stores and a <c>[DelegateCommand]</c> on one of its instance methods, the
/// latter reported at the attribute because the property it generates is generated code.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoesisCommandLeakAnalyzer : DiagnosticAnalyzer
{
    public const string SelfCapturingCommandId = "NTK2102";

    private const string FrameworkElementMetadataName = "Noesis.FrameworkElement";
    private const string CommandMetadataName = "System.Windows.Input.ICommand";
    private const string CommandAttributeMetadataName =
        "NoesisToolkit.Mvvm.DelegateCommandAttribute";

    private static readonly DiagnosticDescriptor SelfCapturingCommandRule = new(
        SelfCapturingCommandId,
        title: "Command captures the control that exposes it",
        messageFormat: "'{0}' captures the control that exposes it; bound into that control's own template it pins the control for the process lifetime — take the owner from a static handler's TemplatedParent, or move the command to a ViewModel",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Setting a native Command property makes Noesis hold the managed command, which "
            + "in turn holds every object its delegates captured. A control that binds such a command "
            + "into its own template closes a reference cycle through native code: the element keeps "
            + "the template child alive, the child keeps the command alive, and the command keeps the "
            + "element's managed proxy alive, so the element is never destroyed. Commands belong on a "
            + "ViewModel; a control's own buttons are better served by a static Click handler that "
            + "reads its TemplatedParent."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(SelfCapturingCommandRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var frameworkElement = start.Compilation.GetTypeByMetadataName(
                FrameworkElementMetadataName
            );
            var command = start.Compilation.GetTypeByMetadataName(CommandMetadataName);
            if (frameworkElement is null || command is null)
                return;

            start.RegisterOperationAction(
                operation => AnalyzeCreation(operation, frameworkElement, command),
                OperationKind.ObjectCreation
            );

            if (
                start.Compilation.GetTypeByMetadataName(CommandAttributeMetadataName) is
                { } attribute
            )
            {
                start.RegisterSymbolAction(
                    symbol => AnalyzeCommandMethod(symbol, frameworkElement, attribute),
                    SymbolKind.Method
                );
            }
        });
    }

    private static void AnalyzeCreation(
        OperationAnalysisContext context,
        INamedTypeSymbol frameworkElement,
        INamedTypeSymbol command
    )
    {
        if (
            context.ContainingSymbol.ContainingType is not { } owner
            || !TypeWalk.InheritsFrom(owner, frameworkElement)
        )
            return;

        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is null || !Implements(creation.Type, command))
            return;

        if (!creation.Arguments.Any(argument => InstanceCapture.Captures(argument.Value)))
            return;

        // Only a command the element keeps can be reached from its own template.
        if (StoredMember(creation) is not { } member)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(SelfCapturingCommandRule, creation.Syntax.GetLocation(), member.Name)
        );
    }

    private static void AnalyzeCommandMethod(
        SymbolAnalysisContext context,
        INamedTypeSymbol frameworkElement,
        INamedTypeSymbol attribute
    )
    {
        if (
            context.Symbol is not IMethodSymbol { IsStatic: false } method
            || !TypeWalk.InheritsFrom(method.ContainingType, frameworkElement)
        )
            return;

        var marker = method
            .GetAttributes()
            .FirstOrDefault(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute)
            );

        if (marker?.ApplicationSyntaxReference is not { } reference)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                SelfCapturingCommandRule,
                Location.Create(reference.SyntaxTree, reference.Span),
                method.Name + "Command"
            )
        );
    }

    private static bool Implements(ITypeSymbol type, INamedTypeSymbol command) =>
        type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, command));

    private static ISymbol? StoredMember(IOperation creation)
    {
        var node = creation;
        while (node.Parent is IConversionOperation conversion)
            node = conversion;

        return node.Parent switch
        {
            ISimpleAssignmentOperation assignment => OwnMember(assignment.Target),
            ICoalesceAssignmentOperation lazy => OwnMember(lazy.Target),
            IFieldInitializerOperation field => field.InitializedFields.FirstOrDefault(),
            IPropertyInitializerOperation property =>
                property.InitializedProperties.FirstOrDefault(),
            _ => null,
        };
    }

    private static ISymbol? OwnMember(IOperation target) =>
        target switch
        {
            IPropertyReferenceOperation { Instance: IInstanceReferenceOperation } property =>
                property.Property,
            IFieldReferenceOperation { Instance: IInstanceReferenceOperation } field => field.Field,
            _ => null,
        };
}
