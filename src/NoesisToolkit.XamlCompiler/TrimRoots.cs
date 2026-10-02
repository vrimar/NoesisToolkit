using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

sealed class TrimRoots
{
    enum Kind
    {
        Member,
        Constructor,
        Fields,
    }

    readonly struct Root(INamedTypeSymbol type, string member, Kind kind) : IEquatable<Root>
    {
        public INamedTypeSymbol Type { get; } = type;
        public string Member { get; } = member;
        public Kind Kind { get; } = kind;

        public bool Equals(Root other) =>
            Kind == other.Kind
            && Member == other.Member
            && SymbolEqualityComparer.Default.Equals(Type, other.Type);

        public override bool Equals(object? obj) => obj is Root other && Equals(other);

        public override int GetHashCode() =>
            (SymbolEqualityComparer.Default.GetHashCode(Type) * 31 + Member.GetHashCode()) * 31
            + (int)Kind;
    }

    readonly List<Root> _roots = new List<Root>();

    readonly HashSet<Root> _seen = new HashSet<Root>();

    public int Count => _roots.Count;

    public IEnumerable<INamedTypeSymbol> Types => _roots.Select(r => r.Type);

    public void Constructor(INamedTypeSymbol type)
    {
        if (
            Rootable(type)
            && type.TypeKind == TypeKind.Class
            && !type.IsAbstract
            && !type.IsStatic
            && type.InstanceConstructors.Any(c =>
                c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public
            )
        )
            Add(new Root(type, "", Kind.Constructor));
    }

    public void EnumLiterals(ITypeSymbol type)
    {
        if (
            Unwrapped(type) is INamedTypeSymbol { TypeKind: TypeKind.Enum } named
            && Rootable(named)
        )
            Add(new Root(named, "", Kind.Fields));
    }

    // The member must exist once every generator has run, or the trimmer warns it resolved nothing.
    public void Member(INamedTypeSymbol owner, string name)
    {
        if (Rootable(owner))
            Add(new Root(owner, name, Kind.Member));
    }

    // Noesis' lookup finds the most derived override, so every managed base keeps its own.
    public void Overrides(ITypeSymbol type)
    {
        for (
            var current = Unwrapped(type) as INamedTypeSymbol;
            current is { SpecialType: SpecialType.None } && Rootable(current);
            current = current.BaseType
        )
        {
            foreach (var signature in ProbedOverrides.DeclaredBy(current))
                Add(new Root(current, signature, Kind.Member));
        }
    }

    public void Truncate(int count)
    {
        for (var i = _roots.Count - 1; i >= count; i--)
            _seen.Remove(_roots[i]);

        _roots.RemoveRange(count, _roots.Count - count);
    }

    void Add(Root root)
    {
        if (_seen.Add(root))
            _roots.Add(root);
    }

    static bool Rootable(INamedTypeSymbol type) =>
        type.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum
        && type.ContainingAssembly is { } assembly
        && assembly.Name != "Noesis.GUI"
        && !type.IsAnonymousType
        && !type.IsTupleType;

    public static ITypeSymbol Unwrapped(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
            } nullable
            ? nullable.TypeArguments[0]
            : type;

    public List<string> Render(Compilation compilation)
    {
        var lines = new List<string>();
        if (!DynamicDependencies.Available(compilation))
            return lines;

        foreach (var root in _roots)
        {
            var member = root.Kind switch
            {
                Kind.Constructor =>
                    $"{DynamicDependencies.MemberTypes}.PublicParameterlessConstructor",
                Kind.Fields => $"{DynamicDependencies.MemberTypes}.PublicFields",
                _ => DynamicDependencies.Named(root.Member),
            };

            if (DynamicDependencies.Attribute(member, root.Type, compilation) is { } line)
                lines.Add(line);
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }
}
