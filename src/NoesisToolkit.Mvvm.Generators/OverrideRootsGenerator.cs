using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Mvvm.Generators;

/// <summary>
/// Keeps the overrides Noesis looks for by reflection when it first meets a managed type: it calls
/// <c>MeasureOverride</c>, <c>OnApplyTemplate</c> and the rest only where it finds them declared
/// below its own base, and an object's own <c>ToString</c> and <c>Equals</c> only where it finds
/// those overridden, on any type it is handed. A trimmed build keeps an override's code for the
/// virtual call but not its metadata, so the lookup finds the base method and the override is
/// silently never called.
/// <para>
/// The roots ride a module initializer, the one member every build keeps for as long as the
/// assembly is loaded, because a control constructed only from code offers nothing else to hang
/// them on.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class OverrideRootsGenerator : IIncrementalGenerator
{
    internal const string OverridesStep = "NoesisMvvmOverrideRoots";

    readonly record struct Probe(string Attribute);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var probes = context
            .SyntaxProvider.CreateSyntaxProvider(
                static (node, _) =>
                    node switch
                    {
                        MethodDeclarationSyntax method => Overrides(
                            method.Modifiers,
                            method.Identifier.ValueText
                        ),
                        PropertyDeclarationSyntax property => Overrides(
                            property.Modifiers,
                            property.Identifier.ValueText
                        ),
                        RecordDeclarationSyntax => true,
                        _ => false,
                    },
                static (ctx, ct) =>
                    Read(
                        ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct),
                        ctx.SemanticModel.Compilation
                    )
            )
            .SelectMany(static (found, _) => found)
            .WithTrackingName(OverridesStep)
            .Collect();

        var holder = context.CompilationProvider.Select(
            static (compilation, _) => HolderOf(compilation)
        );

        context.RegisterSourceOutput(
            probes.Combine(holder),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right)
        );
    }

    static bool Overrides(SyntaxTokenList modifiers, string name) =>
        ProbedOverrides.Named(name) && modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword));

    static ImmutableArray<Probe> Read(ISymbol? symbol, Compilation compilation)
    {
        if (symbol is INamedTypeSymbol declared)
            return Probes(declared, ProbedOverrides.DeclaredBy(declared), compilation);

        return symbol?.ContainingType is { } owner && ProbedOverrides.Signature(symbol) is { } one
            ? Probes(owner, [one], compilation)
            : ImmutableArray<Probe>.Empty;
    }

    static ImmutableArray<Probe> Probes(
        INamedTypeSymbol type,
        IEnumerable<string> signatures,
        Compilation compilation
    ) =>
        signatures
            .Select(s =>
                DynamicDependencies.Attribute(DynamicDependencies.Named(s), type, compilation)
            )
            .OfType<string>()
            .Select(a => new Probe(a))
            .ToImmutableArray();

    static string? HolderOf(Compilation compilation)
    {
        if (
            !DynamicDependencies.Available(compilation)
            || compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.ModuleInitializerAttribute"
            )
                is not { } initializer
            || !compilation.IsSymbolAccessibleWithin(initializer, compilation.Assembly)
        )
            return null;

        var name = new string(
            (compilation.AssemblyName ?? "").Where(char.IsLetterOrDigit).ToArray()
        );
        return "NoesisToolkitGenerated."
            + (
                name.Length == 0 || char.IsDigit(name[0]) ? "_" + name
                : SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name
                : name
            );
    }

    static void Emit(SourceProductionContext spc, ImmutableArray<Probe> probes, string? holder)
    {
        if (holder is null || probes.IsDefaultOrEmpty)
            return;

        var w = new CodeWriter();
        Output.Header(w);

        using (w.Block($"namespace {holder}"))
        using (w.Block("internal static class NoesisOverrideRoots"))
        {
            w.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
            foreach (
                var attribute in probes
                    .Select(p => p.Attribute)
                    .Distinct()
                    .OrderBy(a => a, StringComparer.Ordinal)
            )
                w.Line(attribute);

            w.Line("internal static void Keep() { }");
        }

        Output.Add(spc, "NoesisOverrideRoots.g.cs", w.ToString());
    }
}
