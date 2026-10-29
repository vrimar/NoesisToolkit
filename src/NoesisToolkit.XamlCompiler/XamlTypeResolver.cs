using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>
/// Resolves XAML names against the Roslyn compilation: an xmlns URI plus a local name to a type,
/// a type plus an attribute name to a settable property, and a type plus a property name to the
/// <c>DependencyProperty</c> that backs it.
/// </summary>
internal sealed class XamlTypeResolver(Compilation compilation, XamlCompilerOptions options)
{
    public const string PresentationNs =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public const string DirectiveNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string BehaviorsNs = "http://schemas.microsoft.com/xaml/behaviors";
    public const string CompatibilityNs =
        "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public const string ToolkitNs = XamlScopeRules.ToolkitNamespace;

    readonly Dictionary<string, INamedTypeSymbol?> _cache = new Dictionary<
        string,
        INamedTypeSymbol?
    >(StringComparer.Ordinal);

    public ITypeSymbol ObjectType => compilation.GetSpecialType(SpecialType.System_Object);

    public ITypeSymbol UInt32Type => compilation.GetSpecialType(SpecialType.System_UInt32);

    /// <summary>The type an element's tag names.</summary>
    public INamedTypeSymbol? SymbolOf(System.Xml.Linq.XElement element) =>
        Resolve(element.Name.NamespaceName, element.Name.LocalName);

    public INamedTypeSymbol? Resolve(string namespaceUri, string localName)
    {
        var key = namespaceUri + "|" + localName;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = ResolveCore(namespaceUri, localName);
        _cache[key] = result;
        return result;
    }

    INamedTypeSymbol? ResolveCore(string namespaceUri, string localName)
    {
        foreach (var ns in CandidateNamespaces(namespaceUri))
        {
            var qualified = ns.Length == 0 ? localName : $"{ns}.{localName}";
            var symbol = TypeLookup.ByName(compilation, qualified);
            if (symbol is not null)
                return symbol;

            // GetTypeByMetadataName returns null when two referenced assemblies define the name.
            var matches = compilation
                .GetTypesByMetadataName(qualified)
                .Where(t => t.DeclaredAccessibility == Accessibility.Public)
                .ToArray();
            if (matches.Length > 0)
                return matches[0];
        }

        return null;
    }

    public bool Maps(string namespaceUri) => CandidateNamespaces(namespaceUri).Any();

    public static bool NativeOwned(string namespaceUri) =>
        XamlMarkup.IsNoesisExtensions(namespaceUri);

    IEnumerable<string> CandidateNamespaces(string namespaceUri)
    {
        if (namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var body = namespaceUri.Substring("clr-namespace:".Length);
            var semi = body.IndexOf(';');
            return new[] { semi < 0 ? body : body.Substring(0, semi) };
        }

        return options.ClrNamespacesFor(namespaceUri);
    }

    /// <summary>Whether XAML appends children to this property rather than assigning it.</summary>
    public static bool IsCollection(ITypeSymbol type) =>
        type.SpecialType != SpecialType.System_String
        && (
            type.AllInterfaces.Any(i => i.ToDisplayString() == "System.Collections.IList")
            || type.GetMembers("Add")
                .OfType<IMethodSymbol>()
                .Any(m => !m.IsStatic && m.Parameters.Length == 1)
        );

    /// <summary>Whether the type declares a public constructor taking these parameter types.</summary>
    // Compared by name: a reference set that defines one of these twice resolves it to nothing.
    public bool HasConstructor(INamedTypeSymbol type, params string[] parameterTypes) =>
        type.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public
            && c.Parameters.Length == parameterTypes.Length
            && c.Parameters.Select((p, i) => MetadataName(p.Type) == parameterTypes[i])
                .All(match => match)
        );

    static string MetadataName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString() + "." + type.MetadataName
            : type.MetadataName;

    /// <summary>Whether the type declares a static <c>Parse(string)</c> returning itself.</summary>
    public static bool HasStringParse(ITypeSymbol type) =>
        type.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .Any(m =>
                m.IsStatic
                && m.Parameters.Length == 1
                && m.Parameters[0].Type.SpecialType == SpecialType.System_String
                && SymbolEqualityComparer.Default.Equals(m.ReturnType, type)
            );

    public static bool HasCulturedParse(ITypeSymbol type) =>
        type.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .Any(m =>
                m.IsStatic
                && m.Parameters.Length == 2
                && m.Parameters[0].Type.SpecialType == SpecialType.System_String
                && m.Parameters[1].Type.ToDisplayString() == "System.IFormatProvider"
                && SymbolEqualityComparer.Default.Equals(m.ReturnType, type)
            );

    /// <summary>A type and everything it inherits from, nearest first.</summary>
    static IEnumerable<ITypeSymbol> BaseChain(ITypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            yield return t;
    }

    /// <summary>The static <c>Get{name}</c> attached-property getter, if the owner declares one.</summary>
    public IMethodSymbol? FindAttachedGetter(ITypeSymbol owner, string name) =>
        BaseChain(owner)
            .SelectMany(t => t.GetMembers("Get" + name).OfType<IMethodSymbol>())
            .FirstOrDefault(m =>
                m is { IsStatic: true, DeclaredAccessibility: Accessibility.Public }
                && m.Parameters.Length == 1
            );

    // From the code-behind class where there is one, else from a generated type of this assembly.
    public bool IsAccessible(
        ISymbol member,
        INamedTypeSymbol? within,
        ITypeSymbol? through = null
    ) =>
        within is null
            ? compilation.IsSymbolAccessibleWithin(member, compilation.Assembly)
            : compilation.IsSymbolAccessibleWithin(member, within, through);

    public INamedTypeSymbol Nameable(INamedTypeSymbol type, INamedTypeSymbol? within)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (IsAccessible(current, within))
                return current;
        }

        return (INamedTypeSymbol)ObjectType;
    }

    public bool Converts(ITypeSymbol from, ITypeSymbol to) =>
        compilation.ClassifyCommonConversion(from, to) is { Exists: true, IsImplicit: true };

    public static bool IsGenericDefinition(INamedTypeSymbol type) =>
        type.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter);

    public static bool DeclaresRoutedEvent(ITypeSymbol type, string name) =>
        BaseChain(type)
            .SelectMany(t => t.GetMembers(name + "Event"))
            .Any(m =>
                m
                    is IPropertySymbol
                        {
                            IsStatic: true,
                            DeclaredAccessibility: Accessibility.Public
                        }
                        or IFieldSymbol
                        {
                            IsStatic: true,
                            DeclaredAccessibility: Accessibility.Public
                        }
            );

    public IEventSymbol? FindEvent(ITypeSymbol type, string name) =>
        BaseChain(type)
            .SelectMany(t => t.GetMembers(name).OfType<IEventSymbol>())
            .FirstOrDefault(e => e.DeclaredAccessibility == Accessibility.Public);

    public IPropertySymbol? FindProperty(ITypeSymbol type, string name)
    {
        foreach (var t in BaseChain(type))
        {
            var match = Declared(t, name);
            if (match is not null)
                return match;
        }

        // An interface has no base type, so what it inherits is only reachable this way.
        foreach (var inherited in type.AllInterfaces)
        {
            var match = Declared(inherited, name);
            if (match is not null)
                return match;
        }

        return null;
    }

    static IPropertySymbol? Declared(ITypeSymbol type, string name) =>
        type.GetMembers(name)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.DeclaredAccessibility == Accessibility.Public);

    const string DelegateCommandAttribute = "NoesisToolkit.Mvvm.DelegateCommandAttribute";

    /// <summary>The type of the command property <c>[DelegateCommand]</c> asks for. That generator
    /// runs in this same pass, so what it emits is never in the compilation to look up; the
    /// attribute on the method is the only statement that the property will exist.</summary>
    public ITypeSymbol? FindGeneratedCommand(ITypeSymbol type, string name)
    {
        const string suffix = "Command";
        if (!name.EndsWith(suffix, StringComparison.Ordinal) || name.Length == suffix.Length)
            return null;

        var methodName = name.Substring(0, name.Length - suffix.Length);

        foreach (
            var method in BaseChain(type)
                .SelectMany(t => t.GetMembers(methodName).OfType<IMethodSymbol>())
        )
        {
            if (
                !method
                    .GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == DelegateCommandAttribute)
            )
                continue;

            var kind = method.ReturnsVoid
                ? "NoesisToolkit.Mvvm.DelegateCommand"
                : "NoesisToolkit.Mvvm.AsyncDelegateCommand";

            if (method.Parameters.Length == 0)
                return compilation.GetTypeByMetadataName(kind);

            return compilation.GetTypeByMetadataName(kind + "`1") is { } generic
                ? generic.Construct(method.Parameters[0].Type)
                : null;
        }

        return null;
    }

    const string DependencyPropertyAttribute = "NoesisToolkit.Mvvm.DependencyPropertyAttribute";

    /// <summary>The type that declares <c>{name}Property</c>, for qualifying the emitted reference.
    /// A type in this same compilation may only be promised one by <c>[DependencyProperty]</c>,
    /// whose generator runs in this same pass, so the attribute stands in for the field.</summary>
    public INamedTypeSymbol? FindDependencyPropertyOwner(ITypeSymbol type, string name)
    {
        var field = name + "Property";
        foreach (var t in BaseChain(type))
        {
            var found = t.GetMembers(field)
                .Any(m =>
                    m
                        is IPropertySymbol
                            {
                                IsStatic: true,
                                DeclaredAccessibility: Accessibility.Public
                            }
                            or IFieldSymbol
                            {
                                IsStatic: true,
                                DeclaredAccessibility: Accessibility.Public
                            }
                );
            if (found || HasDependencyPropertyAttribute(t, name))
                return t as INamedTypeSymbol;
        }

        return null;
    }

    static bool HasDependencyPropertyAttribute(ITypeSymbol type, string name) =>
        type.GetMembers(name).OfType<IPropertySymbol>().Any(IsGeneratedDependencyProperty);

    static bool IsGeneratedDependencyProperty(IPropertySymbol property) =>
        property
            .GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == DependencyPropertyAttribute);

    readonly Dictionary<string, bool> _twoWayByDefault = new Dictionary<string, bool>(
        StringComparer.Ordinal
    );

    public bool? DeclaredTwoWayByDefault(ITypeSymbol type, string name)
    {
        if (
            FindDependencyPropertyOwner(type, name) is not { } declaring
            || Framework(declaring.ContainingAssembly.Name)
        )
            return null;

        var key = Fqn(declaring) + "." + name;
        if (!_twoWayByDefault.TryGetValue(key, out var twoWay))
            _twoWayByDefault[key] = twoWay = ReadTwoWayByDefault(declaring, name);

        return twoWay;
    }

    bool ReadTwoWayByDefault(INamedTypeSymbol declaring, string name)
    {
        if (
            compilation
                .GetTypeByMetadataName(MetadataOptions)
                ?.GetMembers("BindsTwoWayByDefault")
                .OfType<IFieldSymbol>()
                .FirstOrDefault()
                ?.ConstantValue
            is not { } twoWay
        )
            return false;

        var attribute = declaring
            .GetMembers(name)
            .OfType<IPropertySymbol>()
            .SelectMany(p => p.GetAttributes())
            .FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == DependencyPropertyAttribute
            );

        var options = attribute?.AttributeConstructor is { } constructor
            ? AttributeOptions(attribute, constructor)
            : RegisteredOptions(declaring, name);

        // Metadata this compilation cannot read is only known at run time, so it may bind two-way.
        return options is not { } flags || (flags & Convert.ToInt64(twoWay)) != 0;
    }

    const string MetadataOptions = "Noesis.FrameworkPropertyMetadataOptions";

    static long? AttributeOptions(AttributeData attribute, IMethodSymbol constructor)
    {
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            if (
                constructor.Parameters[i].Name == "metadataOptions"
                && i < attribute.ConstructorArguments.Length
            )
                return attribute.ConstructorArguments[i].Value is { } value
                    ? Convert.ToInt64(value)
                    : null;
        }

        return 0;
    }

    long? RegisteredOptions(INamedTypeSymbol declaring, string name)
    {
        if (
            declaring
                .GetMembers(name + "Property")
                .OfType<IFieldSymbol>()
                .FirstOrDefault()
                ?.DeclaringSyntaxReferences.FirstOrDefault()
                ?.GetSyntax()
            is not VariableDeclaratorSyntax { Initializer.Value: { } registration }
        )
            return null;

        if (
            compilation.GetSemanticModel(registration.SyntaxTree).GetOperation(registration)
            is not IInvocationOperation invocation
        )
            return null;

        var metadata = invocation
            .Arguments.Select(a => Unconverted(a.Value))
            .OfType<IObjectCreationOperation>()
            .FirstOrDefault(c =>
                c.Type is { } type && DerivesFrom(type, "global::Noesis.PropertyMetadata")
            );
        if (metadata?.Type is not { } metadataType)
            return 0;

        if (!DerivesFrom(metadataType, "global::Noesis.FrameworkPropertyMetadata"))
            return 0;

        if (
            metadata.Initializer?.Initializers.Any(i =>
                i
                    is ISimpleAssignmentOperation
                    {
                        Target: IPropertyReferenceOperation
                        {
                            Property.Name: "BindsTwoWayByDefault"
                        },
                    }
            ) == true
        )
            return null;

        var argument = metadata.Arguments.FirstOrDefault(a =>
            a.Parameter?.Type.ToDisplayString() == MetadataOptions
        );
        if (argument is null)
            return 0;

        return argument.Value.ConstantValue is { HasValue: true, Value: { } value }
            ? Convert.ToInt64(value)
            : null;
    }

    static IOperation Unconverted(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
            operation = conversion.Operand;

        return operation;
    }

    /// <summary>The type an attached property takes. <c>[DependencyProperty]</c> on a static property
    /// generates the <c>Set{name}</c> in this same pass, so the property it is on answers where the
    /// setter is not there to ask.</summary>
    public ITypeSymbol? FindAttachedValueType(ITypeSymbol owner, string name)
    {
        if (FindAttachedSetter(owner, name) is { } setter)
            return setter.Parameters[1].Type;

        return BaseChain(owner)
            .SelectMany(t => t.GetMembers(name).OfType<IPropertySymbol>())
            .FirstOrDefault(p => p.IsStatic && IsGeneratedDependencyProperty(p))
            ?.Type;
    }

    /// <summary>The static <c>Set{name}</c> attached-property setter, if the owner declares one.</summary>
    public IMethodSymbol? FindAttachedSetter(ITypeSymbol owner, string name) =>
        BaseChain(owner)
            .SelectMany(t => t.GetMembers("Set" + name).OfType<IMethodSymbol>())
            .FirstOrDefault(m =>
                m is { IsStatic: true, DeclaredAccessibility: Accessibility.Public }
                && m.Parameters.Length == 2
            );

    /// <summary>The property named by <c>[ContentProperty]</c> anywhere up the base chain.</summary>
    public string? FindContentProperty(ITypeSymbol type) =>
        BaseChain(type)
            .SelectMany(t => t.GetAttributes())
            .FirstOrDefault(a =>
                a.AttributeClass?.Name == "ContentPropertyAttribute"
                && a.ConstructorArguments.Length == 1
            )
            ?.ConstructorArguments[0]
            .Value as string;

    // Each document builds its own resolver, and every one of them walks the same compilation.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Compilation,
        List<INamedTypeSymbol>
    > Applications = new();

    public IReadOnlyList<INamedTypeSymbol> ApplicationTypes() =>
        Applications.GetValue(compilation, CollectApplicationTypes);

    static List<INamedTypeSymbol> CollectApplicationTypes(Compilation compilation)
    {
        var types = new List<INamedTypeSymbol>();
        Collect(compilation.Assembly.GlobalNamespace);
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!Framework(reference.Name))
                Collect(reference.GlobalNamespace);
        }

        return types;

        void Collect(INamespaceOrTypeSymbol container)
        {
            foreach (var member in container.GetMembers())
            {
                if (member is INamedTypeSymbol type)
                    types.Add(type);

                if (member is INamespaceOrTypeSymbol nested)
                    Collect(nested);
            }
        }
    }

    public static bool Framework(string assembly) =>
        assembly is "mscorlib" or "netstandard" or "Noesis.GUI" or "System" or "Microsoft"
        || assembly.StartsWith("System.", StringComparison.Ordinal)
        || assembly.StartsWith("Microsoft.", StringComparison.Ordinal);

    public static bool DerivesFrom(ITypeSymbol type, string fullName) =>
        BaseChain(type).Any(t => Fqn(t) == fullName);

    public static string Fqn(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
