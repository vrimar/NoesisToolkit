using System;
using System.Collections.Generic;
using System.Linq;

namespace NoesisToolkit.Xaml;

/// <summary>How a XAML path on disk becomes the names the generated code is keyed by.</summary>
static class XamlPaths
{
    /// <summary>The project-relative form of an already-normalised path, or null where it falls
    /// outside the project and so has no name the registry can key by.</summary>
    static string? Relativise(string normalised, string projectDir)
    {
        var root = projectDir.Replace('\\', '/').TrimEnd('/');
        if (root.Length == 0)
            return null;

        // The separator is what stops a sibling directory sharing the root's prefix.
        return normalised.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)
            ? normalised.Substring(root.Length + 1)
            : null;
    }

    /// <summary>The path a Source= uri is resolved against: the document's path within the project.</summary>
    internal static string RelativePath(string path, string projectDir)
    {
        var full = path.Replace('\\', '/');
        return Relativise(full, projectDir) ?? System.IO.Path.GetFileName(full);
    }

    /// <summary>The pack name a Source= uri uses for this file: prefix plus project-relative path.</summary>
    internal static string LogicalName(string path, string prefix, string projectDir) =>
        prefix + ";" + RelativePath(path, projectDir);

    /// <summary>The pack name a Source= uri names, resolved against the document that wrote it.
    /// A uri already carrying an assembly is absolute and answers for itself.</summary>
    internal static string SourceLogicalName(
        string source,
        string filePath,
        string prefix,
        string projectDir
    )
    {
        var uri = source.Replace('\\', '/');

        // Ahead of the ';' test below, which a pack uri would otherwise satisfy unchanged.
        var component = uri.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        if (component >= 0)
        {
            var slash = uri.LastIndexOf('/', component);
            return (
                    slash < 0
                        ? uri.Substring(0, component)
                        : uri.Substring(slash + 1, component - slash - 1)
                )
                + ";"
                + uri.Substring(component + ";component/".Length);
        }

        var rooted = uri.StartsWith("/", StringComparison.Ordinal);
        uri = uri.TrimStart('/');
        if (uri.Contains(";"))
            return uri;

        var directory = rooted
            ? ""
            : System.IO.Path.GetDirectoryName(filePath.Replace('\\', '/'))?.Replace('\\', '/')
                ?? "";

        var segments = new List<string>();
        var escaped = false;
        foreach (var segment in (directory.Length > 0 ? directory + "/" + uri : uri).Split('/'))
        {
            if (segment == "." || segment.Length == 0)
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    escaped = true;
                else
                    segments.RemoveAt(segments.Count - 1);

                continue;
            }

            segments.Add(segment);
        }

        // The walk drops the empty leading segment, and Relativise compares against a rooted root.
        var combined = directory.Length > 0 ? directory + "/" + uri : uri;
        var normalised =
            (combined.StartsWith("/", StringComparison.Ordinal) ? "/" : "")
            + string.Join("/", segments.ToArray());

        // Prefixing an escaped uri would key a registry row that can never match.
        return escaped ? uri : prefix + ";" + (Relativise(normalised, projectDir) ?? normalised);
    }

    /// <summary>
    /// A type name unique per document. Derived from the whole project-relative path, so two folders
    /// holding the same file name cannot collide.
    /// </summary>
    internal static string TypeNameFor(string path, string projectDir)
    {
        var relative = RelativePath(path, projectDir);
        var name = new string(relative.Where(char.IsLetterOrDigit).ToArray());
        return name + "Xaml";
    }
}
