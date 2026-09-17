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
/// Reported only where the types this compilation can see settle the question: the declared type and
/// every source type deriving from or implementing it, since any of them may sit behind the
/// DataContext. An abstract type or interface nothing here implements, a polymorphic type declared in
/// another assembly, and <see langword="object"/> say nothing about the instance, so those paths go
/// unchecked.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class XamlBindingAnalyzer : DiagnosticAnalyzer
{
    public const string UnresolvedBindingId = "NTK2001";
    public const string UnresolvedNamespaceId = "NTK2002";
    public const string UnresolvedStaticId = "NTK2003";
    public const string UndeclaredContextId = "NTK2004";
    public const string UnresolvedEnumValueId = "NTK2005";

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
            + "'ntk:DataType' attribute naming the type its DataContext resolves to",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A document carrying an ntk:CompileBindings marker asks for every binding in it "
            + "to be checked, which is only possible where the DataContext type is stated."
    );

    private static readonly DiagnosticDescriptor UnresolvedEnumValueRule = new(
        UnresolvedEnumValueId,
        title: "Enum value does not resolve",
        messageFormat: "'{0}' is not a member of '{1}'; this throws when the element first lays out",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Noesis parses an enum-valued attribute from its string at load time, so a "
            + "member left behind by a rename reaches the lookup as a name nothing maps."
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            UnresolvedBindingRule,
            UnresolvedNamespaceRule,
            UnresolvedStaticRule,
            UnresolvedEnumValueRule,
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

            var subtypes = Subtypes(start.Compilation);
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

                foreach (var reference in TokenScanner.Scan(content))
                    VerifyEnumValue(ctx, reference, namespaces, text);

                var scan = BindingScanner.ScanAll(
                    content,
                    (candidate, wanted) => DerivesFrom(ctx.Compilation, candidate, wanted)
                );
                foreach (var check in scan.Checks)
                    Verify(ctx, check, text, subtypes);

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

    private static void VerifyEnumValue(
        AdditionalFileAnalysisContext context,
        TokenReference reference,
        Dictionary<string, string> namespaces,
        SourceText text
    )
    {
        if (!namespaces.TryGetValue(reference.Prefix, out var @namespace))
            return;

        var type = TypeLookup.ByName(context.Compilation, $"{@namespace}.{reference.TypeName}");
        if (type is null)
            return;

        var enumType = reference.IsExtension
            ? FirstEnumProperty(type)
            : (FindMember(type, reference.Member) as IPropertySymbol)?.Type as INamedTypeSymbol;

        if (enumType is not { TypeKind: TypeKind.Enum } || IsNoesisOwn(enumType))
            return;

        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } && member.Name == reference.Value)
                return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(
                UnresolvedEnumValueRule,
                LocationOf(context.AdditionalFile.Path, reference.Line, reference.Column, text),
                reference.Value,
                enumType.Name
            )
        );
    }

    private static bool IsNoesisOwn(INamedTypeSymbol type)
    {
        for (var ns = type.ContainingNamespace; ns is { IsGlobalNamespace: false }; )
        {
            if (ns.ContainingNamespace is not { IsGlobalNamespace: false })
                return ns.Name == BindingScanner.PresentationNamespace;
            ns = ns.ContainingNamespace;
        }

        return false;
    }

    private static INamedTypeSymbol? FirstEnumProperty(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (
                    property.SetMethod is not null
                    && property.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } @enum
                )
                    return @enum;
            }
        }

        return null;
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
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> subtypes
    )
    {
        var compilation = context.Compilation;
        ITypeSymbol? current = TypeLookup.ByName(compilation, check.ContextType);
        if (current is null)
            return;

        foreach (var hop in check.Hops)
        {
            current = Walk(compilation, current, hop.Path, subtypes, out _, out _);
            if (current is null)
                return;

            if (hop.IsCollection)
            {
                current = ElementTypeOf(current);
                if (current is null)
                    return;
            }
        }

        if (
            Walk(compilation, current, check.Path, subtypes, out var missing, out var owner)
            is not null
        )
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

    private static ITypeSymbol? Walk(
        Compilation compilation,
        ITypeSymbol start,
        string path,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> subtypes,
        out string? missing,
        out ITypeSymbol? owner
    )
    {
        missing = null;
        owner = null;

        var current = start;
        foreach (var segment in path.Split('.'))
        {
            var derived = DerivedHere(current, subtypes);

            if (FindMember(current, segment) is not { } member)
            {
                if (IsConclusiveMiss(compilation, current, segment, derived))
                {
                    missing = segment;
                    owner = current;
                }
                return null;
            }

            if (
                TypeOf(member) is not { } next
                || !SubtypesAgree(derived, segment, next)
                || !CanEnter(compilation, next, subtypes)
            )
                return null;

            current = next;
        }

        return current;
    }

    private static ISymbol? FindMember(ITypeSymbol type, string name) =>
        TypeWalk.FindMember(type, name, interfaces: true);

    private static ITypeSymbol? TypeOf(ISymbol member) =>
        member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };

    private static List<INamedTypeSymbol>? DerivedHere(
        ITypeSymbol type,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> subtypes
    ) =>
        type is INamedTypeSymbol named
        && subtypes.TryGetValue(named.OriginalDefinition, out var derived)
            ? derived
            : null;

    private static bool IsLocal(Compilation compilation, ITypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);

    private static bool IsPolymorphic(ITypeSymbol type) =>
        type.IsAbstract || type.TypeKind == TypeKind.Interface;

    private static bool IsConclusiveMiss(
        Compilation compilation,
        ITypeSymbol type,
        string member,
        List<INamedTypeSymbol>? derived
    )
    {
        if (type.SpecialType == SpecialType.System_Object)
            return false;

        // Another assembly's type can have subtypes this compilation never sees.
        if (!IsLocal(compilation, type))
            return !IsPolymorphic(type) && derived is null;

        if (derived is null)
            return !IsPolymorphic(type);

        foreach (var subtype in derived)
        {
            if (FindMember(subtype, member) is not null)
                return false;
        }

        return true;
    }

    private static bool SubtypesAgree(
        List<INamedTypeSymbol>? derived,
        string member,
        ITypeSymbol declared
    )
    {
        if (derived is null)
            return true;

        foreach (var subtype in derived)
        {
            if (
                FindMember(subtype, member) is { } redeclared
                && TypeOf(redeclared) is { } type
                && !SymbolEqualityComparer.Default.Equals(type, declared)
            )
                return false;
        }

        return true;
    }

    // An unseen subtype can hide a member with one of another type.
    private static bool CanEnter(
        Compilation compilation,
        ITypeSymbol type,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> subtypes
    )
    {
        if (type.SpecialType == SpecialType.System_Object)
            return false;

        var derived = DerivedHere(type, subtypes);
        if (!IsPolymorphic(type) && derived is null)
            return true;

        return IsLocal(compilation, type) && derived is not null;
    }

    private static Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> Subtypes(
        Compilation compilation
    )
    {
        var byAncestor = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(
            SymbolEqualityComparer.Default
        );

        foreach (var type in SourceTypes(compilation.Assembly.GlobalNamespace))
        {
            for (
                var b = type.BaseType;
                b is { SpecialType: not SpecialType.System_Object };
                b = b.BaseType
            )
                Add(b.OriginalDefinition, type);

            foreach (var implemented in type.AllInterfaces)
                Add(implemented.OriginalDefinition, type);
        }

        return byAncestor;

        void Add(INamedTypeSymbol ancestor, INamedTypeSymbol type)
        {
            if (!byAncestor.TryGetValue(ancestor, out var list))
                byAncestor[ancestor] = list = [];
            list.Add(type);
        }
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
