using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NoesisToolkit.CodeGen;

// A raw Location is not equatable and would defeat incremental caching.
readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location? location) =>
        location?.SourceTree is null
            ? null
            : new LocationInfo(
                location.SourceTree.FilePath,
                location.SourceSpan,
                location.GetLineSpan().Span
            );
}
