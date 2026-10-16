using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NoesisToolkit.CodeGen;

static class XamlFiles
{
    public const string ExtensionsProperty = "build_property.NoesisXamlExtensions";

    public const string EncodedExtensionsProperty = "build_property._NoesisXamlExtensionsEncoded";

    public static string Extensions(AnalyzerConfigOptions options) =>
        Property(options, EncodedExtensionsProperty, ExtensionsProperty).Trim()
            is { Length: > 0 } set
            ? set
            // Restated from the packages' props for a consumer that takes the analyzer by project
            // reference, where those props never load.
            : ".xaml";

    // ';' and '#' cut an editorconfig value short, so a packaged build hands it over percent-encoded.
    public static string Property(AnalyzerConfigOptions options, string encoded, string raw) =>
        options.TryGetValue(encoded, out var value) && value is not null
            ? Uri.UnescapeDataString(value)
        : options.TryGetValue(raw, out var plain) && plain is not null ? plain
        : "";

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
