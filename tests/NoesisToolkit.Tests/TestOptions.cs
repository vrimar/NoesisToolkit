using Microsoft.CodeAnalysis.Diagnostics;
using NoesisToolkit.Xaml;

namespace NoesisToolkit.Tests;

static class TestOptions
{
    public static XamlCompilerOptions With(string? namespaceMap = null, string? extensions = null)
    {
        var values = new Dictionary<string, string>();
        if (namespaceMap is not null)
            values["build_property.NoesisXamlNamespaces"] = namespaceMap;
        if (extensions is not null)
            values["build_property.NoesisXamlExtensions"] = extensions;

        return XamlCompilerOptions.Read(new OptionsProvider(values));
    }
}
