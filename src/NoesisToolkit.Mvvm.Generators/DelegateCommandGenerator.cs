using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Mvvm.Generators;

[Generator]
public sealed class DelegateCommandGenerator : IIncrementalGenerator
{
    const string AttributeMetadataName = "NoesisToolkit.Mvvm.DelegateCommandAttribute";

    static readonly DiagnosticDescriptor NotPartial = new(
        id: "NTK3101",
        title: "Containing type must be partial",
        messageFormat: "Type '{0}' must be declared partial to generate its commands",
        category: "NoesisToolkit",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor BadReturnType = new(
        id: "NTK3102",
        title: "Command method must return void or ValueTask",
        messageFormat: "'{0}' returns '{1}'; a [DelegateCommand] method must return void or ValueTask",
        category: "NoesisToolkit",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    static readonly DiagnosticDescriptor ExtraParameters = new(
        id: "NTK3103",
        title: "Command method takes more than one parameter",
        messageFormat: "'{0}' takes {1} parameters; a command passes only the first",
        category: "NoesisToolkit",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    readonly record struct Command(
        OwnerInfo Owner,
        string MethodName,
        string ReturnTypeDisplay,
        bool ReturnsVoid,
        bool ReturnsValueTask,
        int ParameterCount,
        string? FirstParameterFqn,
        LocationInfo? MethodLocation
    );

    internal const string CommandsStep = "NoesisMvvmCommands";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => Read((IMethodSymbol)ctx.TargetSymbol)
            )
            .WithTrackingName(CommandsStep)
            .Collect();

        context.RegisterSourceOutput(commands, static (spc, all) => Emit(spc, all));
    }

    static Command Read(IMethodSymbol method)
    {
        var type = method.ContainingType;
        var firstParam = method.Parameters.FirstOrDefault();

        return new Command(
            OwnerInfo.From(type, Constraints(type)),
            method.Name,
            method.ReturnType.ToDisplayString(),
            method.ReturnsVoid,
            ReturnsValueTask(method),
            method.Parameters.Length,
            firstParam?.Type.Fq(),
            LocationInfo.From(method.Locations.FirstOrDefault())
        );
    }

    static void Emit(SourceProductionContext spc, ImmutableArray<Command> all)
    {
        foreach (var group in all.GroupBy(c => c.Owner.Fqn, StringComparer.Ordinal))
        {
            var owner = group.First();

            if (!owner.Owner.IsPartial)
            {
                spc.ReportDiagnostic(
                    Diagnostic.Create(
                        NotPartial,
                        owner.Owner.Location?.ToLocation(),
                        owner.Owner.NotPartial ?? owner.Owner.Display
                    )
                );
                continue;
            }

            var members = new CodeWriter();
            var any = false;

            foreach (var command in group.GroupBy(c => c.MethodName).Select(g => g.First()))
            {
                if (!command.ReturnsVoid && !command.ReturnsValueTask)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            BadReturnType,
                            command.MethodLocation?.ToLocation(),
                            command.MethodName,
                            command.ReturnTypeDisplay
                        )
                    );
                    continue;
                }

                if (command.ParameterCount > 1)
                {
                    spc.ReportDiagnostic(
                        Diagnostic.Create(
                            ExtraParameters,
                            command.MethodLocation?.ToLocation(),
                            command.MethodName,
                            command.ParameterCount
                        )
                    );
                    continue;
                }

                var kind = command.ReturnsVoid
                    ? "global::NoesisToolkit.Mvvm.DelegateCommand"
                    : "global::NoesisToolkit.Mvvm.AsyncDelegateCommand";
                var commandType = command.FirstParameterFqn is null
                    ? kind
                    : $"{kind}<{command.FirstParameterFqn}>";

                members.Line($"public {commandType} {command.MethodName}Command =>");
                using (members.Indented())
                    members.Line($"field ??= new({command.MethodName}!);");
                any = true;
            }

            if (!any)
                continue;

            var w = new CodeWriter();
            Output.Header(w);
            if (owner.Owner.Namespace is not null)
                w.Line($"namespace {owner.Owner.Namespace};");
            w.Line();

            // No accessibility modifier: stating one would conflict with an internal declaration.
            var scopes = PartialTypeEmitter.Open(
                w,
                owner.Owner.Declarations,
                owner.Owner.Constraints
            );
            w.Line(members.ToString().TrimEnd('\n'));
            PartialTypeEmitter.Close(scopes);

            Output.Add(spc, $"{owner.Owner.Key}.DelegateCommands.g.cs", w.ToString());
        }
    }

    static bool ReturnsValueTask(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { Name: "ValueTask", IsGenericType: false } named
        && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

    // A string, not a list: an array is reference-equal and would defeat incremental caching.
    static string Constraints(INamedTypeSymbol type)
    {
        if (type.Arity == 0)
            return "";

        var clauses = new List<string>();
        foreach (var p in type.TypeParameters)
        {
            var parts = new List<string>();

            if (p.HasNotNullConstraint)
                parts.Add("notnull");
            if (p.HasUnmanagedTypeConstraint)
                parts.Add("unmanaged");
            else if (p.HasValueTypeConstraint)
                parts.Add("struct");
            else if (p.HasReferenceTypeConstraint)
                parts.Add("class");

            parts.AddRange(p.ConstraintTypes.Select(t => t.Fq()));

            if (p.HasConstructorConstraint)
                parts.Add("new()");

            if (parts.Count > 0)
                clauses.Add($"where {p.Name} : {string.Join(", ", parts)}");
        }

        return string.Join("\n", clauses);
    }
}
