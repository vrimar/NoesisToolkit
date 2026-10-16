using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>
/// The MSBuild-supplied inputs, carried through the incremental pipeline as plain strings so the
/// value stays structurally equatable and a cached step is not invalidated on every compilation.
/// </summary>
readonly record struct XamlCompilerOptions(
    string PackPrefix,
    string ProjectDir,
    string Extensions,
    string Namespaces,
    bool Trimmed
)
{
    public const string PackPrefixProperty = "build_property.NoesisXamlPackPrefix";
    public const string NamespacesProperty = "build_property.NoesisXamlNamespaces";
    public const string EncodedNamespacesProperty = "build_property._NoesisXamlNamespacesEncoded";
    public const string ProjectDirProperty = "build_property.ProjectDir";
    public const string EncodedProjectDirProperty = "build_property._NoesisXamlProjectDirEncoded";

    // EnableTrimAnalyzer=false is a speed switch; the project still trims if anything else says so.
    static readonly string[] TrimProperties =
    [
        "build_property.PublishAot",
        "build_property.PublishTrimmed",
        "build_property.IsTrimmable",
        "build_property.IsAotCompatible",
        "build_property.EnableTrimAnalyzer",
    ];

    public static XamlCompilerOptions Read(AnalyzerConfigOptionsProvider provider)
    {
        var options = provider.GlobalOptions;
        return new XamlCompilerOptions(
            Value(options, PackPrefixProperty),
            XamlFiles.Property(options, EncodedProjectDirProperty, ProjectDirProperty).Trim(),
            XamlFiles.Extensions(options),
            XamlFiles.Property(options, EncodedNamespacesProperty, NamespacesProperty).Trim(),
            TrimProperties.Any(key =>
                string.Equals(Value(options, key), "true", StringComparison.OrdinalIgnoreCase)
            )
        );
    }

    static string Value(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value) && value is not null ? value.Trim() : "";

    /// <summary>The pack name files are keyed by; the assembly name when nothing overrides it.</summary>
    public string PrefixFor(Compilation compilation) =>
        PackPrefix.Length > 0 ? PackPrefix : compilation.AssemblyName ?? "Application";

    public bool Matches(string path) => XamlFiles.Matches(path, Extensions);

    /// <summary>
    /// CLR namespaces to try for an xmlns URI, in order. Built-ins come first and user entries
    /// extend them, so mapping the behaviors URI adds a namespace rather than losing Noesis'.
    /// </summary>
    public IEnumerable<string> ClrNamespacesFor(string namespaceUri)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var builtin in BuiltIn(namespaceUri))
        {
            seen.Add(builtin);
            yield return builtin;
        }

        foreach (var entry in Split(Namespaces, ';', '\r', '\n'))
        {
            var equals = entry.IndexOf('=');
            if (equals <= 0)
                continue;

            if (
                !string.Equals(
                    entry.Substring(0, equals).Trim(),
                    namespaceUri,
                    StringComparison.Ordinal
                )
            )
                continue;

            foreach (var ns in entry.Substring(equals + 1).Split(','))
            {
                var trimmed = ns.Trim();
                if (trimmed.Length > 0 && seen.Add(trimmed))
                    yield return trimmed;
            }
        }
    }

    static IEnumerable<string> BuiltIn(string namespaceUri)
    {
        if (namespaceUri is XamlTypeResolver.PresentationNs or "")
        {
            yield return "Noesis";
            yield return "Noesis.Interactivity";
        }
        else if (namespaceUri == XamlTypeResolver.BehaviorsNs)
        {
            yield return "Noesis.Interactivity";
        }
    }

    static IEnumerable<string> Split(string value, params char[] separators)
    {
        foreach (var part in value.Split(separators))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }
}
