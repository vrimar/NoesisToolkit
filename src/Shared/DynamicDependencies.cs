using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace NoesisToolkit.CodeGen;

static class DynamicDependencies
{
    public const string MemberTypes =
        "global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes";

    public static bool Available(Compilation compilation) =>
        compilation.GetTypeByMetadataName(
            "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute"
        )
            is { } attribute
        && compilation.IsSymbolAccessibleWithin(attribute, compilation.Assembly);

    // Null for a file-local type: its metadata name is mangled, so neither form can reach it.
    public static string? Attribute(
        string member,
        INamedTypeSymbol type,
        Compilation compilation
    ) =>
        FileLocal(type)
            ? null
            : $"[global::System.Diagnostics.CodeAnalysis.DynamicDependency({member}, {TypeArguments(type, compilation)})]";

    public static string Named(string name) => Quote(name);

    public static string Signature(IMethodSymbol method) =>
        method.Parameters.Length == 0
            ? method.Name
            : method.Name
                + "("
                + string.Join(",", method.Parameters.Select(p => DocumentationId(p.Type)))
                + ")";

    static string TypeArguments(INamedTypeSymbol type, Compilation compilation)
    {
        var definition = type.OriginalDefinition;
        if (Open(type))
        {
            if (Nameable(definition, compilation))
                return $"typeof({Fqn(definition.ConstructUnboundGenericType())})";
        }
        else if (Nameable(type, compilation))
        {
            return $"typeof({Fqn(type)})";
        }

        return $"{Quote(DocumentationId(definition))}, {Quote(definition.ContainingAssembly.Name)}";
    }

    static string DocumentationId(ITypeSymbol type)
    {
        var id = type.GetDocumentationCommentId() ?? type.ToDisplayString();
        return id.StartsWith("T:", StringComparison.Ordinal) ? id.Substring(2) : id;
    }

    static bool Open(ITypeSymbol type) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => Open(array.ElementType),
            IPointerTypeSymbol pointer => Open(pointer.PointedAtType),
            INamedTypeSymbol named => named.TypeArguments.Any(Open)
                || (named.ContainingType is { } outer && Open(outer)),
            _ => false,
        };

    static bool FileLocal(INamedTypeSymbol type)
    {
        for (
            INamedTypeSymbol? current = type;
            current is not null;
            current = current.ContainingType
        )
        {
            if (current.IsFileLocal)
                return true;
        }

        return false;
    }

    static bool Nameable(ITypeSymbol type, Compilation compilation) =>
        type switch
        {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => Nameable(array.ElementType, compilation),
            INamedTypeSymbol named => !FileLocal(named)
                && compilation.IsSymbolAccessibleWithin(
                    named.OriginalDefinition,
                    compilation.Assembly
                )
                && named.TypeArguments.All(a => Nameable(a, compilation))
                && (named.ContainingType is not { } outer || Nameable(outer, compilation)),
            _ => false,
        };

    static string Fqn(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
