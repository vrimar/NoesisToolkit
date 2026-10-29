using System;
using System.Collections.Generic;
using System.IO;
using Noesis;
using Path = System.IO.Path;

namespace NoesisToolkit.Testing;

/// <summary>
/// Proves the XAML compiler builds what the Noesis parser builds: a document is built both ways,
/// each graph is realized and rendered as text, and the two texts must match.
/// <para>
/// Templates are applied to a host and laid out before they are walked, so what is compared is
/// what the engine instantiates. Values are compared as the engine reads them back, with no data
/// context, so a binding shows the value it holds while unresolved. Timing is not compared: both
/// sides are compared after the application resources are installed, so a lookup the compiled path
/// performs too early still resolves here.
/// </para>
/// <para>
/// Call from the Noesis thread, after <c>GUI.Init</c>, with a <see cref="XamlProvider"/> installed
/// that serves the documents and with the application resources the documents expect.
/// </para>
/// </summary>
public static class XamlParity
{
    /// <summary>Builds every document of a generated <c>XamlRegistry.Dictionaries</c> both ways and
    /// reports each one whose graphs differ or that fails to build.</summary>
    /// <param name="documents">The registry's entries: a document's path, the pack name the parser
    /// loads it by, and the compiled builder.</param>
    /// <param name="flush">The generated <c>XamlResources.Flush</c> of the assembly that compiled
    /// the documents, which settles each compiled dictionary as the application would.</param>
    /// <param name="dumps">A directory to write both renderings of every document to, or null.</param>
    /// <returns>One failure per document that differs, plus one where no document rendered
    /// anything at all; empty when every document matches.</returns>
    /// <remarks>A merged dictionary that names another document by <c>Source</c> is named rather
    /// than walked again where both sides merge the same contents in the same place; anywhere else
    /// its contents are walked where it is merged.</remarks>
    public static IReadOnlyList<XamlParityFailure> Check(
        IEnumerable<(string Path, string Logical, Func<ResourceDictionary> Build)> documents,
        Action<ResourceDictionary> flush,
        string? dumps = null
    )
    {
        if (documents is null)
            throw new ArgumentNullException(nameof(documents));

        if (flush is null)
            throw new ArgumentNullException(nameof(flush));

        var failures = new List<XamlParityFailure>();
        var rendered = false;
        foreach (var (path, logical, build) in documents)
        {
            try
            {
                // The uri a document is reached by becomes its BaseUri, so it has to be rooted.
                var parsed = (ResourceDictionary)GUI.LoadXaml("/" + logical);
                var compiled = build();
                flush(compiled);

                var (expected, actual) = XamlGraphDump.Dump(parsed, compiled, expandMerged: false);
                rendered |= expected.Length > 0;

                if (Judge(path, expected, actual, dumps) is { } failure)
                    failures.Add(failure);
            }
            catch (Exception exception)
            {
                failures.Add(
                    new XamlParityFailure(path, $"{exception.GetType().Name}: {exception.Message}")
                );
            }
        }

        // Empty renderings agree, which would pass a run whose parser loaded nothing at all.
        if (!rendered)
            failures.Add(new XamlParityFailure("", "the parser built nothing for any document"));

        return failures;
    }

    /// <summary>Compares a dictionary the parser built with one the compiler built for the same
    /// document.</summary>
    /// <param name="document">The name to report a difference under, and to write dumps by.</param>
    /// <param name="parsed">What the parser built.</param>
    /// <param name="compiled">What the compiled builder built, already flushed.</param>
    /// <param name="expandMerged">True to walk every merged dictionary, as for a whole application
    /// graph; false to name a merged document instead of walking it again where both sides merge
    /// the same contents in the same place.</param>
    /// <param name="dumps">A directory to write both renderings to, or null.</param>
    /// <returns>The first difference, or null where the two match.</returns>
    public static XamlParityFailure? Compare(
        string document,
        ResourceDictionary parsed,
        ResourceDictionary compiled,
        bool expandMerged = false,
        string? dumps = null
    )
    {
        if (parsed is null)
            throw new ArgumentNullException(nameof(parsed));

        if (compiled is null)
            throw new ArgumentNullException(nameof(compiled));

        var (expected, actual) = XamlGraphDump.Dump(parsed, compiled, expandMerged);
        return Judge(document, expected, actual, dumps);
    }

    /// <summary>Compares an <c>x:Class</c> root the parser filled with one
    /// <c>InitializeComponent</c> filled, its own local values included.</summary>
    /// <param name="document">The name to report a difference under, and to write dumps by.</param>
    /// <param name="parsed">A root the parser loaded.</param>
    /// <param name="compiled">A root whose generated <c>InitializeComponent</c> ran.</param>
    /// <param name="dumps">A directory to write both renderings to, or null.</param>
    /// <returns>The first difference, or null where the two match.</returns>
    /// <remarks>Both roots are put in a view of their own and laid out.</remarks>
    public static XamlParityFailure? Compare(
        string document,
        FrameworkElement parsed,
        FrameworkElement compiled,
        string? dumps = null
    )
    {
        if (parsed is null)
            throw new ArgumentNullException(nameof(parsed));

        if (compiled is null)
            throw new ArgumentNullException(nameof(compiled));

        var (expected, actual) = XamlGraphDump.Dump(parsed, compiled);

        // Two bare roots agree, which would pass a tree that never realized.
        return VisualTreeHelper.GetChildrenCount(parsed) == 0
            ? new XamlParityFailure(document, "the parser built nothing below the root")
            : Judge(document, expected, actual, dumps);
    }

    static XamlParityFailure? Judge(string document, string expected, string actual, string? dumps)
    {
        if (dumps is not null)
        {
            Directory.CreateDirectory(dumps);
            var name = document.Replace('/', '.').Replace('\\', '.');
            File.WriteAllText(Path.Combine(dumps, name + ".parsed.txt"), expected);
            File.WriteAllText(Path.Combine(dumps, name + ".compiled.txt"), actual);
        }

        if (expected == actual)
            return null;

        var left = expected.Split('\n');
        var right = actual.Split('\n');
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i].Trim() : "<end>";
            var b = i < right.Length ? right[i].Trim() : "<end>";
            if (a != b)
                return new XamlParityFailure(
                    document,
                    $"line {i + 1}: parsed '{a}' vs compiled '{b}'"
                );
        }

        return new XamlParityFailure(document, "the renderings differ in whitespace only");
    }
}

/// <summary>A document whose parsed and compiled graphs differ, or that failed to build.</summary>
public sealed class XamlParityFailure
{
    internal XamlParityFailure(string document, string detail)
    {
        Document = document;
        Detail = detail;
    }

    /// <summary>The document, as its registry entry or the caller names it.</summary>
    public string Document { get; }

    /// <summary>Where the two renderings first part, or why the document failed to build.</summary>
    public string Detail { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Document}: {Detail}";
}
