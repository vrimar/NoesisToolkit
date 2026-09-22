using System;
using System.Collections.Generic;
using System.ComponentModel;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The image compiled markup converts a path into, shared per path while any element still
/// shows it. A list that rebinds its rows as it scrolls would otherwise mint a URI and an image per row
/// per bind, and Noesis resolve the texture afresh for each.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ImageSources
{
    // Weak, so a path no element shows any more lets its texture go.
    static readonly Dictionary<string, WeakReference<BitmapImage>> ByUri = new(
        StringComparer.Ordinal
    );

    /// <summary>What Noesis' own converter makes of <paramref name="uri"/>, reused while it lives.</summary>
    /// <param name="uri">A relative or absolute URI.</param>
    /// <returns>The image.</returns>
    public static ImageSource From(string uri)
    {
        Guard.NotNull(uri, nameof(uri));

        if (ByUri.TryGetValue(uri, out var weak) && weak.TryGetTarget(out var image))
            return image;

        image = new BitmapImage(new Uri(uri, UriKind.RelativeOrAbsolute));
        if (weak is null)
            ByUri[uri] = new WeakReference<BitmapImage>(image);
        else
            weak.SetTarget(image);

        return image;
    }
}
