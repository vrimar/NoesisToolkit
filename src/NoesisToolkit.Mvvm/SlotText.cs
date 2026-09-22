using System;
using System.ComponentModel;
using System.Globalization;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>What a text lane formats one value into.</summary>
/// <typeparam name="T">The value's type.</typeparam>
/// <param name="value">The value to format.</param>
/// <param name="destination">Where the text lands.</param>
/// <param name="written">The characters written.</param>
/// <returns>False where the text does not fit in <paramref name="destination"/>.</returns>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate bool SlotFormat<T>(T value, Span<char> destination, out int written);

/// <summary>Writes a binding's text into a caller's buffer as the native engine formats it, so a text
/// lane hands Noesis the characters without a string.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct SlotText
{
    readonly Span<char> _destination;
    int _written;
    bool _overflowed;

    /// <summary>Starts writing at the head of <paramref name="destination"/>.</summary>
    /// <param name="destination">Where the text lands.</param>
    public SlotText(Span<char> destination) => _destination = destination;

    readonly Span<char> Rest => _destination.Slice(_written);

    /// <summary>Appends text as it is.</summary>
    /// <param name="text">The text.</param>
    public void Literal(string text)
    {
        if (_overflowed || !text.AsSpan().TryCopyTo(Rest))
        {
            _overflowed = true;
            return;
        }

        _written += text.Length;
    }

    /// <summary>Appends an integer in the engine's invariant digits.</summary>
    /// <typeparam name="T">The integer type.</typeparam>
    /// <param name="value">The value.</param>
    public void Integer<T>(T value)
        where T : struct, ISpanFormattable => Formatted(value, default);

    /// <summary>Appends an integer through a <c>D</c>, <c>N</c>, <c>F</c>, <c>P</c> or <c>X</c> format.</summary>
    /// <typeparam name="T">The integer type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format, with an optional precision.</param>
    public void Integer<T>(T value, string format)
        where T : struct, ISpanFormattable => Formatted(value, format.AsSpan());

    /// <summary>Appends a number as the engine shows one unformatted.</summary>
    /// <param name="value">The value.</param>
    public void Number(double value)
    {
        if (double.IsPositiveInfinity(value))
            Literal("∞");
        else if (double.IsNegativeInfinity(value))
            Literal("-∞");
        else if (value == 0)
            Literal("0");
        else
            Formatted(value, default);
    }

    /// <summary>Appends a number as the engine shows one unformatted.</summary>
    /// <param name="value">The value.</param>
    public void Number(float value)
    {
        if (float.IsPositiveInfinity(value))
            Literal("∞");
        else if (float.IsNegativeInfinity(value))
            Literal("-∞");
        else if (value == 0)
            Literal("0");
        else
            Formatted(value, default);
    }

    /// <summary>Appends a number through an <c>F</c>, <c>N</c> or <c>P</c> format, rounded as the engine rounds.</summary>
    /// <param name="value">The value.</param>
    /// <param name="format">The format, with an optional precision.</param>
    public void Fixed(double value, string format)
    {
        if (_overflowed)
            return;

        if (SlotConversion.TryText(value, format, Rest, out var written))
            _written += written;
        else
            _overflowed = true;
    }

    /// <summary>Appends a number through an <c>F</c>, <c>N</c> or <c>P</c> format, rounded as the engine rounds.</summary>
    /// <param name="value">The value.</param>
    /// <param name="format">The format, with an optional precision.</param>
    public void Fixed(float value, string format)
    {
        if (_overflowed)
            return;

        if (SlotConversion.TryText(value, format, Rest, out var written))
            _written += written;
        else
            _overflowed = true;
    }

    /// <summary>Ends the text.</summary>
    /// <param name="written">The characters written.</param>
    /// <returns>False where the text did not fit.</returns>
    public readonly bool Done(out int written)
    {
        written = _written;
        return !_overflowed;
    }

    void Formatted<T>(T value, ReadOnlySpan<char> format)
        where T : struct, ISpanFormattable
    {
        if (
            _overflowed
            || !value.TryFormat(Rest, out var written, format, CultureInfo.InvariantCulture)
        )
        {
            _overflowed = true;
            return;
        }

        _written += written;
    }
}
