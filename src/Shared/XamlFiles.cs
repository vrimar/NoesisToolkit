using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NoesisToolkit.CodeGen;

static class XamlFiles
{
    public const string ExtensionsProperty = "build_property.NoesisXamlExtensions";

    public static string Extensions(AnalyzerConfigOptions options) =>
        options.TryGetValue(ExtensionsProperty, out var value)
        && value?.Trim() is { Length: > 0 } set
            ? set
            // Restated from the packages' props for a consumer that takes the analyzer by project
            // reference, where those props never load.
            : ".xaml";

    public static bool Matches(string path, string extensions)
    {
        foreach (var part in extensions.Split(';', ',', '\r', '\n'))
        {
            var extension = part.Trim();
            if (extension.Length == 0)
                continue;

            var suffix = extension[0] == '.' ? extension : "." + extension;
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
