using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Analyzers;

/// <summary>
/// <b>NTK2001</b> — a <c>{Binding}</c> naming a member its DataContext does not have. Noesis resolves
/// binding paths reflectively at load, so a stale path renders nothing instead of failing, and a
/// rename that misses the XAML leaves no trace until someone notices a blank field in game.
/// <para>
/// Reported only where the declared type settles the question. An abstract type, an interface or
/// <see langword="object"/> says nothing about the instance behind it, so those paths go unchecked.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class XamlBindingAnalyzer : DiagnosticAnalyzer
{
    public const string UnresolvedBindingId = "NTK2001";
    public const string UnresolvedNamespaceId = "NTK2002";
    public const string UnresolvedStaticId = "NTK2003";
    public const string UndeclaredContextId = "NTK2004";

    private const string EnabledProperty = "build_property.NoesisAnalyzeXamlBindings";

    private static readonly DiagnosticDescriptor UnresolvedBindingRule = new(
        UnresolvedBindingId,
        title: "Binding path does not resolve",
        messageFormat: "'{0}' does not contain a definition for '{1}'; this binding renders nothing",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Noesis resolves binding paths reflectively at load time, so a path that no "
            + "longer matches its ViewModel fails silently rather than at build time."
    );

    private static readonly DiagnosticDescriptor UnresolvedNamespaceRule = new(
        UnresolvedNamespaceId,
        title: "clr-namespace does not resolve",
        messageFormat: "'{0}' matches no namespace here; every type reached through '{1}:' fails to load",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Noesis resolves a clr-namespace reflectively at load time and only logs when it "
            + "misses, so a namespace left behind by a rename disables every control that uses it."
    );

    private static readonly DiagnosticDescriptor UnresolvedStaticRule = new(
        UnresolvedStaticId,
        title: "x:Static does not resolve",
        messageFormat: "'{0}' does not resolve; the target receives null instead",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Noesis resolves x:Static reflectively at load time and only logs when it misses, "
            + "so a renamed member leaves the target holding null rather than failing the build."
    );

    private static readonly DiagnosticDescriptor UndeclaredContextRule = new(
        UndeclaredContextId,
        title: "Binding has no declared DataContext type",
        messageFormat: "'{0}' cannot be checked: annotate the enclosing template with an "
            + "'ntk:DataType' comment naming the type its DataContext resolves to",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A document carrying an ntk:CompileBindings marker asks for every binding in it "
            + "to be checked, which is only possible where the DataContext type is stated."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            UnresolvedBindingRule,
            UnresolvedNamespaceRule,
            UnresolvedStaticRule,
            UndeclaredContextRule
        );

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(start =>
        {
            var options = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (!IsTrue(options, EnabledProperty))
                return;

            var subclassed = Subclassed(start.Compilation);
            var extensions = XamlFiles.Extensions(options);

            start.RegisterAdditionalFileAction(ctx =>
            {
                if (!XamlFiles.Matches(ctx.AdditionalFile.Path, extensions))
                    return;

                var text = ctx.AdditionalFile.GetText(ctx.CancellationToken);
                if (text is null)
                    return;

                var content = text.ToString();

                var namespaces = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [""] = BindingScanner.PresentationNamespace,
                };
                foreach (var declaration in BindingScanner.ScanNamespaces(content))
                {
                    namespaces[declaration.Prefix] = declaration.Namespace;
                    VerifyNamespace(ctx, declaration, text);
                }

                foreach (var reference in BindingScanner.ScanStaticReferences(content))
                    VerifyStatic(ctx, reference, namespaces, text);

                var scan = BindingScanner.ScanAll(
                    content,
                    (candidate, wanted) => DerivesFrom(ctx.Compilation, candidate, wanted)
                );
                foreach (var check in scan.Checks)
                    Verify(ctx, check, text, subclassed);

                if (!scan.CompileBindings)
                    return;

                foreach (var unresolved in scan.Unresolved)
                    ctx.ReportDiagnostic(
                        Diagnostic.Create(
                            UndeclaredContextRule,
                            LocationOf(
                                ctx.AdditionalFile.Path,
                                unresolved.Line,
                                unresolved.Column,
                                text
                            ),
                            unresolved.Path
                        )
                    );
            });
        });
    }

    private static bool DerivesFrom(Compilation compilation, string candidate, string wanted)
    {
        if (
            TypeLookup.ByName(compilation, candidate) is not { } type
            || TypeLookup.ByName(compilation, wanted) is not { } target
        )
            return false;

        return TypeWalk.InheritsFrom(type, target);
    }

    private static void VerifyNamespace(
        AdditionalFileAnalysisContext context,
        XmlnsDeclaration declaration,
        SourceText text
    )
    {
        if (Resolves(context.Compilation.GlobalNamespace, declaration.Namespace))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                UnresolvedNamespaceRule,
                LocationOf(context.AdditionalFile.Path, declaration.Line, declaration.Column, text),
                declaration.Namespace,
                declaration.Prefix
            )
        );
    }

    private static void VerifyStatic(
        AdditionalFileAnalysisContext context,
        StaticReference reference,
        Dictionary<string, string> namespaces,
        SourceText text
    )
    {
        if (!namespaces.TryGetValue(reference.Prefix, out var @namespace))
            return;

        if (!Resolves(context.Compilation.GlobalNamespace, @namespace))
            return;

        var type = TypeLookup.ByName(context.Compilation, $"{@namespace}.{reference.TypeName}");
        if (type is not null && FindStatic(type, reference.MemberName) is not null)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                UnresolvedStaticRule,
                LocationOf(context.AdditionalFile.Path, reference.Line, reference.Column, text),
                $"{@namespace}.{reference.TypeName}.{reference.MemberName}"
            )
        );
    }

    private static ISymbol? FindStatic(ITypeSymbol type, string name) =>
        TypeWalk.FindMember(type, name, staticOnly: true);

    private static bool Resolves(INamespaceSymbol root, string dotted)
    {
        // clr-namespace: with nothing after it names the global namespace, which always resolves.
        if (dotted.Length == 0)
            return true;

        var current = root;
        foreach (var part in dotted.Split('.'))
        {
            current = current.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
            if (current is null)
                return false;
        }

        return true;
    }

    private static void Verify(
        AdditionalFileAnalysisContext context,
        BindingCheck check,
        SourceText text,
        ImmutableHashSet<INamedTypeSymbol> subclassed
    )
    {
        ITypeSymbol? current = TypeLookup.ByName(context.Compilation, check.ContextType);
        if (current is null)
            return;

        foreach (var hop in check.Hops)
        {
            current = Walk(current, hop.Path, subclassed, out _, out _);
            if (current is null)
                return;

            if (hop.IsCollection)
            {
                current = ElementTypeOf(current);
                if (current is null)
                    return;
            }
        }

        if (Walk(current, check.Path, subclassed, out var missing, out var owner) is not null)
            return;
        if (missing is null || owner is null)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                UnresolvedBindingRule,
                LocationOf(context.AdditionalFile.Path, check.Line, check.Column, text),
                owner.Name,
                missing
            )
        );
    }

    // Only where the containing type is concrete enough for absence to be conclusive.
    private static ITypeSymbol? Walk(
        ITypeSymbol start,
        string path,
        ImmutableHashSet<INamedTypeSymbol> subclassed,
        out string? missing,
        out ITypeSymbol? owner
    )
    {
        missing = null;
        owner = null;

        var current = start;
        foreach (var segment in path.Split('.'))
        {
            if (FindMember(current, segment) is not { } member)
            {
                if (IsConclusive(current, subclassed))
                {
                    missing = segment;
                    owner = current;
                }
                return null;
            }

            var next = member switch
            {
                IPropertySymbol property => property.Type,
                IFieldSymbol field => field.Type,
                _ => null,
            };
            if (next is null || !IsConclusive(next, subclassed))
                return null;

            current = next;
        }

        return current;
    }

    private static ISymbol? FindMember(ITypeSymbol type, string name) =>
        TypeWalk.FindMember(type, name, interfaces: true);

    private static bool IsConclusive(
        ITypeSymbol type,
        ImmutableHashSet<INamedTypeSymbol> subclassed
    ) =>
        type.SpecialType != SpecialType.System_Object
        && type.TypeKind != TypeKind.Interface
        && !type.IsAbstract
        && !(type is INamedTypeSymbol named && subclassed.Contains(named));

    // A subclassed type may hold an instance whose members its declaration does not list.
    private static ImmutableHashSet<INamedTypeSymbol> Subclassed(Compilation compilation)
    {
        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(
            SymbolEqualityComparer.Default
        );
        foreach (var type in SourceTypes(compilation.Assembly.GlobalNamespace))
        {
            for (var b = type.BaseType; b is not null; b = b.BaseType)
                builder.Add(b.OriginalDefinition);
        }
        return builder.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceOrTypeSymbol scope)
    {
        foreach (var member in scope.GetMembers())
        {
            if (member is INamespaceOrTypeSymbol child)
            {
                if (child is INamedTypeSymbol type)
                    yield return type;
                foreach (var nested in SourceTypes(child))
                    yield return nested;
            }
        }
    }

    private static ITypeSymbol? ElementTypeOf(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        var enumerable = (type as INamedTypeSymbol)?.AllInterfaces.FirstOrDefault(i =>
            i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
        );
        return enumerable?.TypeArguments.FirstOrDefault();
    }

    private static Location LocationOf(string path, int lineNumber, int column, SourceText text)
    {
        var line = Math.Max(0, Math.Min(lineNumber - 1, text.Lines.Count - 1));
        var character = Math.Max(0, column - 1);
        var position = new LinePosition(line, character);
        var offset = Math.Min(text.Lines[line].Start + character, text.Length);
        return Location.Create(
            path,
            new TextSpan(offset, 0),
            new LinePositionSpan(position, position)
        );
    }

    private static bool IsTrue(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value)
        && value.Equals("true", StringComparison.OrdinalIgnoreCase);
}
