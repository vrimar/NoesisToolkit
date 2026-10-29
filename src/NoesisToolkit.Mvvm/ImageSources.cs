using System;
using System.Collections.Generic;
using System.ComponentModel;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The image compiled markup converts a path into, shared per path while any element still
/// shows it or it is among the most recently used. A list that rebinds its rows as it scrolls would
/// otherwise mint a URI and an image per row per bind, and Noesis resolve the texture afresh for each;
/// the recent ones outlive the element that last showed them, so a row scrolled back after a
/// collection finds its image still there.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ImageSources
{
    /// <summary>How many of the most recently used images are held past their last element.</summary>
    public const int Recent = 512;

    // Weak, so a path no element shows any more and nothing used lately lets its texture go.
    static readonly Dictionary<string, WeakReference<BitmapImage>> ByUri = new(
        StringComparer.Ordinal
    );

    static readonly BitmapImage?[] RecentlyUsed = new BitmapImage?[Recent];
    static int _nextRecent;
    static int _sweepAt = 2 * Recent;

    /// <summary>What Noesis' own converter makes of <paramref name="uri"/>, reused while it lives.</summary>
    /// <param name="uri">A relative or absolute URI.</param>
    /// <returns>The image, or null where <paramref name="uri"/> is no URI at all.</returns>
    public static ImageSource? From(string uri)
    {
        Guard.NotNull(uri, nameof(uri));

        if (!ByUri.TryGetValue(uri, out var weak) || !weak.TryGetTarget(out var image))
        {
            if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsed))
                return null;

            image = new BitmapImage(parsed);
            if (weak is not null)
                weak.SetTarget(image);
            else
            {
                if (ByUri.Count >= _sweepAt)
                    Sweep();

                ByUri[uri] = new WeakReference<BitmapImage>(image);
            }
        }

        RecentlyUsed[_nextRecent] = image;
        _nextRecent = (_nextRecent + 1) % Recent;
        return image;
    }

    static void Sweep()
    {
        foreach (var (uri, weak) in ByUri)
        {
            if (!weak.TryGetTarget(out _))
                ByUri.Remove(uri);
        }

        _sweepAt = Math.Max(2 * Recent, 2 * ByUri.Count);
    }
}
