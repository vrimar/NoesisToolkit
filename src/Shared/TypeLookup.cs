using Microsoft.CodeAnalysis;

namespace NoesisToolkit.CodeGen;

static class TypeLookup
{
    // XAML writes a generic viewmodel without its arguments, and every consumer closes it
    // differently, so the unbound definition is the most any one document can name.
    const int MaxArity = 4;

    public static INamedTypeSymbol? ByName(Compilation compilation, string metadataName)
    {
        if (compilation.GetTypeByMetadataName(metadataName) is { } exact)
            return exact;

        for (var arity = 1; arity <= MaxArity; arity++)
        {
            if (compilation.GetTypeByMetadataName($"{metadataName}`{arity}") is { } generic)
                return generic;
        }

        return null;
    }
}
