using System.Globalization;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// Changes the value so the dump shows the converter was actually reached.
public sealed class UpperConverter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value?.ToString()?.ToUpperInvariant();

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value?.ToString()?.ToLowerInvariant();
}

public sealed class JoinConverter : IMultiValueConverter
{
    public object? Convert(
        object?[] values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => string.Join(parameter?.ToString() ?? "|", values.Select(v => v?.ToString() ?? "-"));

    public object?[]? ConvertBack(
        object? value,
        Type[] targetTypes,
        object? parameter,
        CultureInfo culture
    ) => null;
}

/// <summary>Joins the values, marking a child whose path ran out, and records what it was handed.</summary>
public sealed class ProbeJoin : IMultiValueConverter
{
    public readonly List<string> Seen = new();

    readonly string _separator;

    public ProbeJoin(string separator = ",") => _separator = separator;

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        Seen.Add(string.Join("|", values.Select(Show)));
        return string.Join((string?)parameter ?? _separator, values.Select(Show));
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();

    static string Show(object? value) =>
        ReferenceEquals(value, DependencyProperty.UnsetValue) ? "<unset>"
        : value is null ? "<null>"
        : $"{value.GetType().Name}:{value}";
}

/// <summary>Prefixes the value, so a dump shows the converter was reached at all.</summary>
public sealed class StampConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? "stamped-null" : $"stamped-{value}";

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}
