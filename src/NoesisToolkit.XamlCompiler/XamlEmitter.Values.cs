using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>Turning XAML text into C# — type references, literals and value conversions.</summary>
sealed partial class XamlEmitter
{
    // XAML spellings with no matching CLR enum member.
    static readonly Dictionary<string, string> EnumAliases = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Ctrl"] = "Control",
        ["Win"] = "Windows",
        ["Esc"] = "Escape",
    };

    INamedTypeSymbol? ResolveTypeSymbol(XElement scope, string reference)
    {
        var trimmed = reference.Trim();
        if (XamlMarkupParser.IsMarkup(trimmed))
        {
            var call = XamlMarkupParser.Parse(trimmed);
            if (call is not { Name: "x:Type" or "Type" })
                return null;

            trimmed = call.Positional.FirstOrDefault() ?? "";
        }

        var colon = trimmed.IndexOf(':');
        var prefix = colon < 0 ? "" : trimmed.Substring(0, colon);
        var localName = colon < 0 ? trimmed : trimmed.Substring(colon + 1);

        var ns =
            prefix.Length == 0
                ? DefaultNamespace(scope)
                : scope.GetNamespaceOfPrefix(prefix)?.NamespaceName;

        return ns is null ? null : resolver.Resolve(ns, localName);
    }

    string? ResolveTypeReference(XElement scope, string reference)
    {
        var symbol = ResolveTypeSymbol(scope, reference);
        return symbol is null ? null : XamlTypeResolver.Fqn(symbol);
    }

    static string DefaultNamespace(XElement scope) =>
        scope.GetDefaultNamespace().NamespaceName is { Length: > 0 } ns
            ? ns
            : XamlTypeResolver.PresentationNs;

    /// <summary>Resolves a name whose xmlns may be empty, which means the document's default.</summary>
    INamedTypeSymbol? ResolveIn(XElement scope, string namespaceUri, string localName) =>
        resolver.Resolve(
            namespaceUri.Length == 0 ? DefaultNamespace(scope) : namespaceUri,
            localName
        );

    /// <summary>An emitted cast, omitted where the slot takes anything.</summary>
    static string CastPrefix(ITypeSymbol? expected) =>
        TakesAnything(expected) ? "" : $"({XamlTypeResolver.Fqn(expected!)})";

    /// <summary>The type name to cast to, or <paramref name="fallback"/> where the slot takes
    /// anything. Unparenthesised, because the caller wraps it itself.</summary>
    static string CastTypeOr(ITypeSymbol? expected, string fallback) =>
        TakesAnything(expected) ? fallback : XamlTypeResolver.Fqn(expected!);

    static bool TakesAnything(ITypeSymbol? expected) =>
        expected is null || expected.SpecialType == SpecialType.System_Object;

    string? ConvertValue(XElement scope, string raw, ITypeSymbol targetType)
    {
        var type = targetType;
        if (
            type is INamedTypeSymbol nullable
            && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
        )
            type = nullable.TypeArguments[0];

        var text = raw.Trim();

        if (type.TypeKind == TypeKind.Enum)
            return EnumLiteral(type, text);

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return Quote(raw);
            case SpecialType.System_Boolean:
                // Anything else would be emitted as a bare identifier.
                return bool.TryParse(text, out var flag) ? (flag ? "true" : "false") : null;
            case SpecialType.System_Single:
                return NumberLiteral(text, "F");
            case SpecialType.System_Double:
                return NumberLiteral(text, "D");
            case SpecialType.System_Decimal:
                return decimal.TryParse(text, Number, Invariant, out _) ? text + "M" : null;
            case SpecialType.System_Char:
                return text.Length == 1 ? Quote(text) : null;
            case SpecialType.System_Int32:
            case SpecialType.System_Int16:
            case SpecialType.System_Int64:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt64:
                return IntegerLiteral(type.SpecialType, text);
            case SpecialType.System_Object:
                return Quote(raw);
        }

        var typeFqn = XamlTypeResolver.Fqn(type);

        switch (typeFqn)
        {
            case "global::System.Type":
            {
                var resolved = ResolveTypeReference(scope, text);
                return resolved is null ? null : $"typeof({resolved})";
            }
            case "global::System.Uri":
            case "global::Noesis.Uri":
                return $"new {typeFqn}({Quote(text)}, global::System.UriKind.RelativeOrAbsolute)";
            case "global::Noesis.Cursor":
                return $"global::Noesis.Cursors.{text}";
            case "global::Noesis.PropertyPath":
                return $"new global::Noesis.PropertyPath({Quote(text)})";
            case "global::Noesis.FontFamily":
                return filePath.Length == 0
                    ? $"new global::Noesis.FontFamily({Quote(text)})"
                    : $"new global::Noesis.FontFamily({BaseUri()}, {Quote(text)})";
            case "global::Noesis.PathFigureCollection":
                return $"((global::Noesis.PathGeometry)global::Noesis.Geometry.Parse({Quote(text)})).Figures";
            case "global::Noesis.Color":
                return ColorLiteral(text);
            case "global::System.TimeSpan":
                return $"global::System.TimeSpan.Parse({Quote(text)}, global::System.Globalization.CultureInfo.InvariantCulture)";
        }

        if (XamlTypeResolver.DerivesFrom(type, "global::Noesis.ImageSource"))
            return $"new global::Noesis.BitmapImage(new global::System.Uri({Quote(text)}, global::System.UriKind.RelativeOrAbsolute))";

        if (typeFqn == "global::Noesis.RoutedEvent")
            return RoutedEventReference(scope, text);

        if (
            typeFqn == "global::System.Windows.Input.ICommand"
            || XamlTypeResolver.DerivesFrom(type, "global::Noesis.RoutedCommand")
        )
            return StaticMemberReference(scope, text);

        if (XamlTypeResolver.HasStringParse(type))
            return $"{typeFqn}.Parse({Quote(text)})";

        if (XamlTypeResolver.DerivesFrom(type, "global::Noesis.Brush"))
            return $"({typeFqn})global::Noesis.Brush.Parse({Quote(text)})";

        return Fail($"no value conversion for '{raw}' into {typeFqn}");
    }

    string? RoutedEventReference(XElement scope, string text)
    {
        var dot = text.LastIndexOf('.');
        if (dot > 0)
        {
            var owner = ResolveTypeReference(scope, text.Substring(0, dot));
            if (owner is not null)
                return $"{owner}.{text.Substring(dot + 1)}Event";
        }

        var styled = StyleTarget;
        for (var t = (ITypeSymbol?)styled; t is not null; t = t.BaseType)
        {
            if (resolver.FindProperty(t, text + "Event") is not null)
                return $"{XamlTypeResolver.Fqn(t)}.{text}Event";
        }

        return $"global::Noesis.FrameworkElement.{text}Event";
    }

    string? StaticMemberReference(XElement scope, string text)
    {
        var dot = text.LastIndexOf('.');
        if (dot <= 0)
        {
            return Fail($"'{text}' is not a qualified static member");
        }

        var ownerSymbol = ResolveTypeSymbol(scope, text.Substring(0, dot));
        if (ownerSymbol is null)
        {
            return Fail($"could not resolve '{text}'");
        }

        var member = text.Substring(dot + 1);
        var owner = XamlTypeResolver.Fqn(ownerSymbol);

        if (resolver.FindProperty(ownerSymbol, member) is not null)
            return $"{owner}.{member}";

        if (resolver.FindProperty(ownerSymbol, member + "Command") is not null)
            return $"{owner}.{member}Command";

        return Fail($"'{owner}' has no member '{member}'");
    }

    string? EnumLiteral(ITypeSymbol type, string text)
    {
        var fqn = XamlTypeResolver.Fqn(type);
        var members = type.GetMembers().OfType<IFieldSymbol>().Where(f => f.IsConst).ToArray();
        var literals = new List<string>();

        foreach (var raw in text.Split(',', '+'))
        {
            var part = raw.Trim();
            if (part.Length == 0)
                continue;

            var match =
                members.FirstOrDefault(m => m.Name == part)
                ?? members.FirstOrDefault(m =>
                    string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase)
                )
                ?? members.FirstOrDefault(m =>
                    EnumAliases.TryGetValue(part, out var actual)
                    && string.Equals(m.Name, actual, StringComparison.Ordinal)
                );

            if (match is null)
            {
                return Fail($"'{part}' is not a member of {fqn}");
            }

            literals.Add($"{fqn}.{match.Name}");
        }

        return literals.Count == 0 ? null : string.Join(" | ", literals.ToArray());
    }

    string? ColorLiteral(string text)
    {
        if (text.StartsWith("#", StringComparison.Ordinal))
        {
            var hex = text.Substring(1);
            if (hex.Length is 3 or 4)
                hex = string.Concat(hex.Select(c => new string(c, 2)));

            if (hex.Length == 6)
                hex = "FF" + hex;

            if (
                hex.Length == 8
                && uint.TryParse(
                    hex,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var packed
                )
            )
            {
                var a = (packed >> 24) & 0xFF;
                var r = (packed >> 16) & 0xFF;
                var g = (packed >> 8) & 0xFF;
                var b = packed & 0xFF;
                return $"global::Noesis.Color.FromArgb({a}, {r}, {g}, {b})";
            }
        }

        return NamedColor(text) is { } named ? $"global::Noesis.Colors.{named}" : null;
    }

    string? NamedColor(string text) =>
        resolver
            .Resolve(XamlTypeResolver.PresentationNs, "Colors")
            ?.GetMembers()
            .FirstOrDefault(m =>
                m is IPropertySymbol or IFieldSymbol
                && string.Equals(m.Name, text, StringComparison.OrdinalIgnoreCase)
            )
            ?.Name;

    const NumberStyles Number = NumberStyles.Float | NumberStyles.AllowThousands;

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    static string? NumberLiteral(string text, string suffix) =>
        text switch
        {
            "Auto" or "NaN" => suffix == "F" ? "float.NaN" : "double.NaN",
            "Infinity" => suffix == "F" ? "float.PositiveInfinity" : "double.PositiveInfinity",
            "-Infinity" => suffix == "F" ? "float.NegativeInfinity" : "double.NegativeInfinity",
            _ => double.TryParse(text, Number, Invariant, out _) ? text + suffix : null,
        };

    static string? IntegerLiteral(SpecialType type, string text)
    {
        if (!long.TryParse(text, NumberStyles.Integer, Invariant, out var value))
            return
                type == SpecialType.System_UInt64
                && ulong.TryParse(text, NumberStyles.Integer, Invariant, out _)
                ? text + "UL"
                : null;

        var fits = type switch
        {
            SpecialType.System_SByte => value >= sbyte.MinValue && value <= sbyte.MaxValue,
            SpecialType.System_Byte => value >= byte.MinValue && value <= byte.MaxValue,
            SpecialType.System_Int16 => value >= short.MinValue && value <= short.MaxValue,
            SpecialType.System_UInt16 => value >= ushort.MinValue && value <= ushort.MaxValue,
            SpecialType.System_Int32 => value >= int.MinValue && value <= int.MaxValue,
            SpecialType.System_UInt32 => value >= uint.MinValue && value <= uint.MaxValue,
            SpecialType.System_UInt64 => value >= 0,
            _ => true,
        };

        return fits ? text : null;
    }

    static string Text(object value) => value as string ?? "";

    static string LiteralGuess(string raw)
    {
        var text = raw.Trim();

        if (bool.TryParse(text, out var flag))
            return flag ? "true" : "false";

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i.ToString(CultureInfo.InvariantCulture)
            : Quote(raw);
    }

    static string Quote(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                case '\u0085':
                case '\u2028':
                case '\u2029':
                    builder
                        .Append("\\u")
                        .Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.Append('"').ToString();
    }
}
