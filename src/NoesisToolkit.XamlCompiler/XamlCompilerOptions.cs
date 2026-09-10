using System;
using System.Collections.Generic;
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
    string Namespaces
)
{
    public const string PackPrefixProperty = "build_property.NoesisXamlPackPrefix";
    public const string NamespacesProperty = "build_property.NoesisXamlNamespaces";
    public const string ProjectDirProperty = "build_property.ProjectDir";

    public static XamlCompilerOptions Read(AnalyzerConfigOptionsProvider provider)
    {
        var options = provider.GlobalOptions;
        return new XamlCompilerOptions(
            Value(options, PackPrefixProperty),
            Value(options, ProjectDirProperty),
            XamlFiles.Extensions(options),
            Value(options, NamespacesProperty)
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
