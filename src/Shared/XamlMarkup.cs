using System;

namespace NoesisToolkit.CodeGen;

internal static class XamlMarkup
{
    public static bool IsIdentifier(string s)
    {
        if (s.Length == 0)
            return false;
        if (!char.IsLetter(s[0]) && s[0] != '_')
            return false;
        for (var i = 1; i < s.Length; i++)
        {
            if (!char.IsLetterOrDigit(s[i]) && s[i] != '_')
                return false;
        }
        return true;
    }

    const string ExtensionsNamespace = "clr-namespace:NoesisGUIExtensions";

    // No managed assembly declares its types: Noesis resolves them natively, whatever assembly= names.
    public static bool IsNoesisExtensions(string namespaceUri) =>
        namespaceUri.StartsWith(ExtensionsNamespace, StringComparison.Ordinal)
        && (
            namespaceUri.Length == ExtensionsNamespace.Length
            || namespaceUri[ExtensionsNamespace.Length] == ';'
        );
}
