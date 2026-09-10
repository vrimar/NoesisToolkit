using System;
using System.Collections.Generic;

namespace NoesisToolkit.CodeGen;

/// <summary>Which property scopes which DataContext. The analyzer refuses a binding it cannot
/// scope and the compiler declines to compile one, so the two have to answer this identically or
/// a document the compiler handles is a build error.</summary>
internal static class XamlScopeRules
{
    // Noesis drops an attribute in an xmlns it does not know, so these never reach the graph.
    internal const string ToolkitNamespace = "https://github.com/vrimar/NoesisToolkit";

    /// <summary>A property whose value is a template, and the property whose value supplies the
    /// DataContext that template is stamped against.</summary>
    internal readonly struct TemplateHost(string sourceProperty, bool isCollection)
    {
        public string SourceProperty { get; } = sourceProperty;

        public bool IsCollection { get; } = isCollection;
    }

    internal static readonly Dictionary<string, TemplateHost> TemplateHosts = new Dictionary<
        string,
        TemplateHost
    >(StringComparer.Ordinal)
    {
        ["ContentTemplate"] = new TemplateHost("Content", false),
        ["ItemTemplate"] = new TemplateHost("ItemsSource", true),
        ["ItemContainerStyle"] = new TemplateHost("ItemsSource", true),
        ["GroupHeaderTemplate"] = new TemplateHost("ItemsSource", true),
        ["CellTemplate"] = new TemplateHost("ItemsSource", true),
        ["HeaderTemplate"] = new TemplateHost("Header", false),
    };

    // ItemContainerStyle looks transparent but applies to a generated container, so it belongs above.
    internal static readonly HashSet<string> TransparentHosts = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "Style",
        "Template",
        "ItemsPanel",
    };

    /// <summary>Elements that detach their content from where it was written, so nothing outside
    /// them says what the DataContext inside is.</summary>
    internal static readonly HashSet<string> ScopeBreakers = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "DataTemplate",
        "ControlTemplate",
        "ItemsPanelTemplate",
        "Style",
    };
}
