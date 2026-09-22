using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Decodes native UTF-8 into the string already decoded for the same text, where Noesis decodes
/// a fresh one on every read. A binding reads its source and target several times per change, and a
/// text box's text once per listener per keystroke.</summary>
static unsafe class NativeStrings
{
    const int Sets = 1024;
    const int Ways = 4;

    // Longer text decodes as Noesis would; pooling it would only pin memory.
    const int MaxPooledBytes = 512;

    static readonly string?[] Slots = new string?[Sets * Ways];
    static readonly byte[] Next = new byte[Sets];

    /// <summary>What Noesis' own decode returns — the empty string for a null pointer — shared per text.</summary>
    internal static string Decode(nint utf8)
    {
        if (utf8 == IntPtr.Zero)
            return string.Empty;

        var bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)utf8);
        if (bytes.IsEmpty)
            return string.Empty;
        if (bytes.Length > MaxPooledBytes)
            return Encoding.UTF8.GetString(bytes);

        var set = HashOf(bytes) & (Sets - 1);
        for (var way = 0; way < Ways; way++)
        {
            if (Slots[set * Ways + way] is { } pooled && Equal(pooled, bytes))
                return pooled;
        }

        var fresh = Encoding.UTF8.GetString(bytes);
        Slots[set * Ways + (Next[set]++ & (Ways - 1))] = fresh;
        return fresh;
    }

    internal static bool Matches(nint utf8, string text)
    {
        if (utf8 == IntPtr.Zero)
            return text.Length == 0;

        var bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)utf8);
        if (bytes.IsEmpty || text.Length == 0)
            return bytes.IsEmpty && text.Length == 0;

        return bytes.Length <= MaxPooledBytes
            ? Equal(text, bytes)
            : string.Equals(Encoding.UTF8.GetString(bytes), text, StringComparison.Ordinal);
    }

    internal static bool TryCopy(nint utf8, Span<char> destination, out int written)
    {
        if (utf8 == IntPtr.Zero)
        {
            written = 0;
            return true;
        }

        var bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)utf8);
        return Utf8.ToUtf16(bytes, destination, out _, out written) == OperationStatus.Done;
    }

    static int HashOf(ReadOnlySpan<byte> bytes)
    {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }

    static bool Equal(string pooled, ReadOnlySpan<byte> utf8)
    {
        // A UTF-16 unit is one to three UTF-8 bytes, and a surrogate pair two units for four.
        if (utf8.Length < pooled.Length || utf8.Length > pooled.Length * 3)
            return false;

        Span<char> chars = stackalloc char[MaxPooledBytes];
        return Utf8.ToUtf16(utf8, chars, out _, out var written) == OperationStatus.Done
            && chars[..written].SequenceEqual(pooled);
    }
}
