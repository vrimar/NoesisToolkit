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
}
