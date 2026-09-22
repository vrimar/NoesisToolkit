using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Turns a value into what a target slot holds the way the native binding engine does,
/// wherever generated code cannot state that inline.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SlotConversion
{
    /// <summary>A converter's result of a type the slot does not declare, which the native engine
    /// converts or rejects by rules of its own.</summary>
    public static readonly object Unconverted = new object();

    // A counter shown as text moves through small values, each of which formats once.
    static readonly string?[] SmallIntegers = new string?[1024];

    // Past the cap a value converts fresh rather than growing the map without bound.
    const int CachedCap = 256;

    /// <summary>A binding site's conversion, kept per source value: the rows of a list and the
    /// steps of a slider reach the slot with the same few values over and over, and each of them
    /// boxes or formats once rather than on every evaluation.</summary>
    /// <typeparam name="TSource">The number or enum the source holds.</typeparam>
    /// <param name="convert">What the slot holds for a source value.</param>
    /// <param name="fallback">What anything other than a <typeparamref name="TSource"/> converts to.</param>
    /// <returns>The conversion for one binding site.</returns>
    public static Func<object?, object?> Cached<TSource>(
        Func<TSource, object?> convert,
        object? fallback
    )
        where TSource : struct
    {
        Guard.NotNull(convert, nameof(convert));

        var results = new Dictionary<TSource, object?>();
        return value =>
        {
            if (value is not TSource source)
                return fallback;

            if (results.TryGetValue(source, out var result))
                return result;

            result = convert(source);
            if (results.Count < CachedCap)
                results[source] = result;

            return result;
        };
    }

    /// <summary>The text the native engine shows for <paramref name="value"/>.</summary>
    /// <param name="value">The value to show.</param>
    /// <returns>The invariant digits, shared for a small value.</returns>
    public static string Text(int value) =>
        (uint)value < (uint)SmallIntegers.Length
            ? SmallIntegers[value] ??= value.ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The text the native engine shows for <paramref name="value"/>.</summary>
    /// <param name="value">The value to show.</param>
    /// <returns>The shortest round-trip form, with infinities as the engine writes them.</returns>
    public static string Text(double value) =>
        double.IsPositiveInfinity(value) ? "∞"
        : double.IsNegativeInfinity(value) ? "-∞"
        : value == 0 ? "0"
        : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The text the native engine shows for <paramref name="value"/>.</summary>
    /// <param name="value">The value to show.</param>
    /// <returns>The shortest round-trip form, with infinities as the engine writes them.</returns>
    public static string Text(float value) =>
        float.IsPositiveInfinity(value) ? "∞"
        : float.IsNegativeInfinity(value) ? "-∞"
        : value == 0 ? "0"
        : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The text the native engine formats <paramref name="value"/> to.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">An <c>F</c>, <c>N</c> or <c>P</c> format, with an optional precision.</param>
    /// <returns>The formatted text.</returns>
    public static string Text(double value, string format)
    {
        Span<char> buffer = stackalloc char[FixedChars];
        return TryText(value, format, buffer, out var written)
            ? new string(buffer.Slice(0, written))
            : Grown(value, format);
    }

    /// <summary>The text the native engine formats <paramref name="value"/> to.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">An <c>F</c>, <c>N</c> or <c>P</c> format, with an optional precision.</param>
    /// <returns>The formatted text.</returns>
    public static string Text(float value, string format)
    {
        Span<char> buffer = stackalloc char[FixedChars];
        return TryText(value, format, buffer, out var written)
            ? new string(buffer.Slice(0, written))
            : Grown(value, format);
    }

    /// <summary>Formats <paramref name="value"/> as the native engine does into <paramref name="destination"/>.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">An <c>F</c>, <c>N</c> or <c>P</c> format, with an optional precision.</param>
    /// <param name="destination">Where the text lands.</param>
    /// <param name="written">The characters written.</param>
    /// <returns>False where the text does not fit in <paramref name="destination"/>.</returns>
    public static bool TryText(double value, string format, Span<char> destination, out int written)
    {
        Guard.NotNull(format, nameof(format));
        if (double.IsNaN(value))
            return TryCopy("NaN", destination, out written);
        if (double.IsInfinity(value))
            return TryCopy(value > 0 ? "∞" : "-∞", destination, out written);

        Span<char> shortest = stackalloc char[ShortestChars];
        Math.Abs(value).TryFormat(shortest, out var length, "R", CultureInfo.InvariantCulture);
        return TryFixed(shortest.Slice(0, length), value < 0, format, destination, out written);
    }

    /// <summary>Formats <paramref name="value"/> as the native engine does into <paramref name="destination"/>.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">An <c>F</c>, <c>N</c> or <c>P</c> format, with an optional precision.</param>
    /// <param name="destination">Where the text lands.</param>
    /// <param name="written">The characters written.</param>
    /// <returns>False where the text does not fit in <paramref name="destination"/>.</returns>
    public static bool TryText(float value, string format, Span<char> destination, out int written)
    {
        Guard.NotNull(format, nameof(format));
        if (float.IsNaN(value))
            return TryCopy("NaN", destination, out written);
        if (float.IsInfinity(value))
            return TryCopy(value > 0 ? "∞" : "-∞", destination, out written);

        Span<char> shortest = stackalloc char[ShortestChars];
        Math.Abs(value).TryFormat(shortest, out var length, "R", CultureInfo.InvariantCulture);
        return TryFixed(shortest.Slice(0, length), value < 0, format, destination, out written);
    }

    // Round-trip digits of a double never pass 24 characters; the text of a huge value can.
    const int ShortestChars = 32;
    const int FixedChars = 128;

    static string Grown(double value, string format)
    {
        for (var size = FixedChars * 4; ; size *= 4)
        {
            var buffer = new char[size];
            if (TryText(value, format, buffer, out var written))
                return new string(buffer, 0, written);
        }
    }

    static string Grown(float value, string format)
    {
        for (var size = FixedChars * 4; ; size *= 4)
        {
            var buffer = new char[size];
            if (TryText(value, format, buffer, out var written))
                return new string(buffer, 0, written);
        }
    }

    static bool TryCopy(string text, Span<char> destination, out int written)
    {
        written = text.AsSpan().TryCopyTo(destination) ? text.Length : 0;
        return written == text.Length;
    }

    // The engine rounds the shortest round-trip digits half away from zero; .NET rounds the exact value.
    static bool TryFixed(
        ReadOnlySpan<char> shortest,
        bool negative,
        string format,
        Span<char> destination,
        out int written
    )
    {
        written = 0;
        var kind = char.ToUpperInvariant(format[0]);
        var precision =
            format.Length > 1
                ? int.Parse(format.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture)
                : 2;

        var exponentAt = shortest.IndexOf('E');
        var mantissa = exponentAt < 0 ? shortest : shortest.Slice(0, exponentAt);
        var dot = mantissa.IndexOf('.');
        var point =
            (dot < 0 ? mantissa.Length : dot)
            + (
                exponentAt < 0
                    ? 0
                    : int.Parse(
                        shortest.Slice(exponentAt + 1),
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture
                    )
            );

        // One spare slot ahead of the digits, for a carry that rounds 9.99 up to 10.0.
        Span<char> buffer = stackalloc char[ShortestChars + 1];
        var count = 0;
        foreach (var c in mantissa)
        {
            if (c != '.')
                buffer[1 + count++] = c;
        }

        var lead = 0;
        while (lead < count && buffer[1 + lead] == '0')
            lead++;

        var digits = buffer.Slice(1 + lead, count - lead);
        point = digits.Length == 0 ? 0 : point - lead;

        if (kind == 'P')
            point += 2;

        var keep = point + precision;

        // A negative rounded to zero keeps its sign when its first digit would have rounded up.
        var signed = keep < 0 && digits.Length > 0 && digits[0] >= '5';

        if (keep < digits.Length)
        {
            var roundsUp = keep >= 0 && digits[keep] >= '5';
            var start = 1 + lead;
            digits = keep > 0 ? digits.Slice(0, keep) : buffer.Slice(start, 0);
            if (roundsUp)
            {
                var i = digits.Length - 1;
                while (i >= 0 && digits[i] == '9')
                    digits[i--] = '0';

                if (i >= 0)
                {
                    digits[i]++;
                }
                else
                {
                    buffer[start - 1] = '1';
                    digits = buffer.Slice(start - 1, digits.Length + 1);
                    point++;
                }
            }
        }

        var text = new SpanWriter(destination);
        if (negative && (signed || digits.IndexOfAnyExcept('0') >= 0))
            text.Put('-');

        if (point <= 0 || digits.Length == 0)
            text.Put('0');
        else
            for (var i = 0; i < point; i++)
            {
                if (kind != 'F' && i > 0 && (point - i) % 3 == 0)
                    text.Put(',');

                text.Put(i < digits.Length ? digits[i] : '0');
            }

        if (precision > 0)
        {
            text.Put('.');
            for (var i = point; i < point + precision; i++)
                text.Put(i >= 0 && i < digits.Length ? digits[i] : '0');
        }

        if (kind == 'P')
        {
            text.Put(' ');
            text.Put('%');
        }

        return text.Done(out written);
    }

    ref struct SpanWriter(Span<char> destination)
    {
        readonly Span<char> _destination = destination;
        int _at;

        public void Put(char c)
        {
            if (_at < _destination.Length)
                _destination[_at] = c;

            _at++;
        }

        public readonly bool Done(out int written)
        {
            written = Math.Min(_at, _destination.Length);
            return _at <= _destination.Length;
        }
    }

    internal static bool Convert(
        DependencyObject receiver,
        DependencyProperty property,
        object value
    )
    {
        // A string slot takes only what the engine boxes itself; any other managed object fails.
        if (property.PropertyType == typeof(string) && !Boxed(value))
            return false;

        // A number into a float or double slot is a plain cast to the engine, which needs no binding.
        if (IsNumber(value))
        {
            if (property.PropertyType == typeof(float))
            {
                DependencyWrite.Value(
                    receiver,
                    property,
                    System.Convert.ToSingle(value, CultureInfo.InvariantCulture)
                );
                return true;
            }

            if (property.PropertyType == typeof(double))
            {
                DependencyWrite.Value(
                    receiver,
                    property,
                    System.Convert.ToDouble(value, CultureInfo.InvariantCulture)
                );
                return true;
            }
        }

        BindingOperations.SetBinding(
            receiver,
            property,
            new Binding { Source = value, Mode = BindingMode.OneTime }
        );
        return true;
    }

    static bool IsNumber(object value) =>
        value
            is double
                or float
                or int
                or long
                or short
                or byte
                or uint
                or ulong
                or ushort
                or sbyte
                or decimal;

    static bool Boxed(object value) =>
        value
            is string
                or bool
                or float
                or double
                or decimal
                or int
                or uint
                or long
                or ulong
                or char
                or short
                or sbyte
                or ushort
                or byte
                or Enum
                or Type
                or Uri
                or BaseComponent
                or Color
                or Point
                or Rect
                or Int32Rect
                or Size
                or Thickness
                or CornerRadius
                or GridLength
                or Duration
                or KeyTime
                or TimeSpan
                or VirtualizationCacheLength
                or Matrix
                or Matrix3D
                or Matrix4;
}
