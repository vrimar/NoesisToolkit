using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;
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
        Guard.NotNull(format, nameof(format));
        return double.IsNaN(value) ? "NaN"
            : double.IsInfinity(value) ? (value > 0 ? "∞" : "-∞")
            : Fixed(Math.Abs(value).ToString("R", CultureInfo.InvariantCulture), value < 0, format);
    }

    /// <summary>The text the native engine formats <paramref name="value"/> to.</summary>
    /// <param name="value">The value to format.</param>
    /// <param name="format">An <c>F</c>, <c>N</c> or <c>P</c> format, with an optional precision.</param>
    /// <returns>The formatted text.</returns>
    public static string Text(float value, string format)
    {
        Guard.NotNull(format, nameof(format));
        return float.IsNaN(value) ? "NaN"
            : float.IsInfinity(value) ? (value > 0 ? "∞" : "-∞")
            : Fixed(Math.Abs(value).ToString("R", CultureInfo.InvariantCulture), value < 0, format);
    }

    // The engine rounds the shortest round-trip digits half away from zero; .NET rounds the exact value.
    static string Fixed(string shortest, bool negative, string format)
    {
        var kind = char.ToUpperInvariant(format[0]);
        var precision =
            format.Length > 1 ? int.Parse(format.Substring(1), CultureInfo.InvariantCulture) : 2;

        var exponentAt = shortest.IndexOf('E');
        var mantissa = exponentAt < 0 ? shortest : shortest.Substring(0, exponentAt);
        var dot = mantissa.IndexOf('.');
        var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        var point =
            (dot < 0 ? mantissa.Length : dot)
            + (
                exponentAt < 0
                    ? 0
                    : int.Parse(shortest.Substring(exponentAt + 1), CultureInfo.InvariantCulture)
            );

        var lead = 0;
        while (lead < digits.Length && digits[lead] == '0')
            lead++;

        digits = digits.Substring(lead);
        point = digits.Length == 0 ? 0 : point - lead;

        if (kind == 'P')
            point += 2;

        var keep = point + precision;

        // A negative rounded to zero keeps its sign when its first digit would have rounded up.
        var signed = keep < 0 && digits.Length > 0 && digits[0] >= '5';

        if (keep < digits.Length)
        {
            var kept = keep > 0 ? digits.Substring(0, keep).ToCharArray() : Array.Empty<char>();
            if (keep >= 0 && digits[keep] >= '5')
            {
                var i = kept.Length - 1;
                while (i >= 0 && kept[i] == '9')
                    kept[i--] = '0';

                if (i >= 0)
                {
                    kept[i]++;
                }
                else
                {
                    kept = ("1" + new string(kept)).ToCharArray();
                    point++;
                }
            }

            digits = new string(kept);
        }

        var whole = new StringBuilder();
        if (point <= 0 || digits.Length == 0)
            whole.Append('0');
        else
            for (var i = 0; i < point; i++)
            {
                if (kind != 'F' && i > 0 && (point - i) % 3 == 0)
                    whole.Append(',');

                whole.Append(i < digits.Length ? digits[i] : '0');
            }

        var text = new StringBuilder();
        if (negative && (signed || digits.Trim('0').Length > 0))
            text.Append('-');

        text.Append(whole);
        if (precision > 0)
        {
            text.Append('.');
            for (var i = point; i < point + precision; i++)
                text.Append(i >= 0 && i < digits.Length ? digits[i] : '0');
        }

        if (kind == 'P')
            text.Append(" %");

        return text.ToString();
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

        BindingOperations.SetBinding(
            receiver,
            property,
            new Binding { Source = value, Mode = BindingMode.OneTime }
        );
        return true;
    }

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
