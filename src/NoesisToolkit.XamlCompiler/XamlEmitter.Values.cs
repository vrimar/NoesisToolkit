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

            trimmed = TypeArgument(call);
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

    // typeof cannot name an unbound generic the way XAML writes it, with no arguments at all.
    string? ResolveTypeReference(XElement scope, string reference) =>
        ResolveTypeSymbol(scope, reference) switch
        {
            null => null,
            var symbol when XamlTypeResolver.IsGenericDefinition(symbol) => Fail(
                $"'{reference.Trim()}' names {symbol.Name}`{symbol.Arity}, which markup cannot close"
            ),
            var symbol when !resolver.IsAccessible(symbol, _rootClass) => Fail(
                $"'{reference.Trim()}' names a type generated code cannot reach"
            ),
            var symbol => XamlTypeResolver.Fqn(symbol),
        };

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

    string? ConvertOrFail(XElement scope, string raw, ITypeSymbol targetType, string property)
    {
        var errors = Errors.Count;
        return ConvertValue(scope, raw, targetType)
            ?? (
                Errors.Count == errors
                    ? Fail(
                        $"'{property}' cannot take '{raw}': it does not convert to {XamlTypeResolver.Fqn(targetType)}"
                    )
                    : null
            );
    }

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
                return decimal.TryParse(text, Number, Invariant, out var number)
                    ? number.ToString(Invariant) + "M"
                    : null;
            case SpecialType.System_Char:
                return raw.Length == 1
                    ? Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(raw[0], quote: true)
                    : null;
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
            {
                // An image's own uri resolves against the document, as its Source does.
                var uri =
                    resolver.SymbolOf(scope) is { } owner
                    && XamlTypeResolver.DerivesFrom(owner, "global::Noesis.ImageSource")
                    && ImageUri(text) is { } joined
                        ? joined
                        : text;
                return $"new {typeFqn}({Quote(uri)}, global::System.UriKind.RelativeOrAbsolute)";
            }
            case "global::Noesis.Cursor":
                return CursorLiteral(text);
            case "global::Noesis.PropertyPath":
                RootPropertyPath(scope, text);
                return $"new global::Noesis.PropertyPath({Quote(NativePath(scope, text))})";
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
            return ImageUri(text) is not { } uri
                    ? Fail($"image source '{text}' is not a uri the parser is known to resolve")
                : typeFqn == "global::Noesis.ImageSource" && MvvmRuntimeReferenced
                    ? $"global::NoesisToolkit.Mvvm.CodeGen.ImageSources.From({Quote(uri)})"
                : $"new global::Noesis.BitmapImage(new global::System.Uri({Quote(uri)}, global::System.UriKind.RelativeOrAbsolute))";

        if (typeFqn == "global::Noesis.RoutedEvent")
            return RoutedEventReference(scope, text);

        if (TakesCommand(type))
            return StaticMemberReference(scope, text);

        if (XamlTypeResolver.HasStringParse(type))
            return XamlTypeResolver.HasCulturedParse(type)
                ? $"{typeFqn}.Parse({Quote(text)}, global::System.Globalization.CultureInfo.InvariantCulture)"
                : $"{typeFqn}.Parse({Quote(text)})";

        if (XamlTypeResolver.DerivesFrom(type, "global::Noesis.Brush"))
            return $"({typeFqn})global::Noesis.Brush.Parse({Quote(text)})";

        return Fail($"no value conversion for '{raw}' into {typeFqn}");
    }

    static readonly System.Text.RegularExpressions.Regex UriScheme = new(
        @"^[A-Za-z][A-Za-z0-9+.\-]+:",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    static readonly System.Text.RegularExpressions.Regex DriveRoot = new(
        @"^[A-Za-z]:[/\\]",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    string? ImageUri(string text)
    {
        var colon = text.IndexOf(':');
        var backslash = text.IndexOf('\\') >= 0;

        if (
            text.StartsWith("/", StringComparison.Ordinal)
            || text.StartsWith("\\", StringComparison.Ordinal)
        )
            return colon >= 0 && backslash ? null : text;

        if (colon >= 0)
        {
            if (DriveRoot.IsMatch(text))
                return text;

            if (UriScheme.IsMatch(text))
                return
                    backslash
                    || (
                        text.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)
                        && !text.Substring(4)
                            .StartsWith("://application:,,,", StringComparison.Ordinal)
                    )
                    ? null
                    : text;

            if (backslash || text.IndexOfAny(new[] { '/', '?', '#' }, 0, colon) < 0)
                return null;
        }

        if (filePath.Length == 0)
            return text;

        // Joined as text: the engine normalises dot segments and separators when UriSource is assigned.
        var document = "/" + XamlPaths.LogicalName(filePath, packPrefix, projectDir);
        return document.Substring(0, document.LastIndexOf('/') + 1) + text;
    }

    string? RoutedEventReference(XElement scope, string text)
    {
        var dot = text.LastIndexOf('.');
        if (dot > 0)
        {
            var name = text.Substring(dot + 1);
            if (
                ResolveTypeSymbol(scope, text.Substring(0, dot)) is { } owner
                && XamlTypeResolver.DeclaresRoutedEvent(owner, name)
            )
                return $"{XamlTypeResolver.Fqn(owner)}.{name}Event";

            return Fail($"'{text}' names no routed event");
        }

        var styled = StyleTarget;
        for (var t = (ITypeSymbol?)styled; t is not null; t = t.BaseType)
        {
            if (
                t.GetMembers(text + "Event")
                    .Any(m =>
                        m is IPropertySymbol { IsStatic: true } or IFieldSymbol { IsStatic: true }
                    )
            )
                return $"{XamlTypeResolver.Fqn(t)}.{text}Event";
        }

        return
            resolver.Resolve(XamlTypeResolver.PresentationNs, "FrameworkElement") is { } element
            && XamlTypeResolver.DeclaresRoutedEvent(element, text)
            ? $"global::Noesis.FrameworkElement.{text}Event"
            : Fail($"'{text}' names no routed event the styled element has");
    }

    string? CursorLiteral(string text) =>
        resolver
            .Resolve(XamlTypeResolver.PresentationNs, "Cursors")
            ?.GetMembers()
            .FirstOrDefault(m =>
                m is IPropertySymbol { IsStatic: true } or IFieldSymbol { IsStatic: true }
                && m.Name == text
            )
            ?.Name
            is { } named
            ? $"global::Noesis.Cursors.{named}"
        : XamlMarkup.IsIdentifier(text) ? Fail($"'{text}' is not a cursor name")
        : $"new global::Noesis.Cursor(new global::System.Uri({Quote(text)}, global::System.UriKind.RelativeOrAbsolute))";

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

        if (CommandMember(ownerSymbol, member) is not { } named)
            return Fail($"'{owner}' has no member '{member}'");

        return ParserReadsCommandUnset(ownerSymbol, member, named)
            ? Fail(UnreadCommand(text))
            : $"{owner}.{named}";
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

    // No thousands separator: the parser reads "1,5" as no number at all, not as fifteen.
    const NumberStyles Number = NumberStyles.Float;

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    static string? NumberLiteral(string text, string suffix)
    {
        var type = suffix == "F" ? "float" : "double";
        if (string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase))
            return type + ".NaN";

        if (!double.TryParse(text, Number, Invariant, out var value))
            return null;

        // Past float's range the cast is infinity, which no literal suffix can spell.
        if (suffix == "F" && !double.IsInfinity(value) && Math.Abs(value) > float.MaxValue)
            value = value > 0 ? double.PositiveInfinity : double.NegativeInfinity;

        return double.IsNaN(value) ? type + ".NaN"
            : double.IsPositiveInfinity(value) ? type + ".PositiveInfinity"
            : double.IsNegativeInfinity(value) ? type + ".NegativeInfinity"
            : value.ToString("R", Invariant) + suffix;
    }

    // An object slot boxes the literal's own type, and no other integer width accepts an Int32.
    static string? IntegerLiteral(SpecialType type, string text)
    {
        if (!long.TryParse(text, NumberStyles.Integer, Invariant, out var value))
            return
                type == SpecialType.System_UInt64
                && ulong.TryParse(text, NumberStyles.Integer, Invariant, out var huge)
                ? huge.ToString(Invariant) + "UL"
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

        if (!fits)
            return null;

        var literal = value.ToString(Invariant);
        return type switch
        {
            SpecialType.System_Int64 => literal + "L",
            SpecialType.System_UInt32 => literal + "U",
            SpecialType.System_UInt64 => literal + "UL",
            SpecialType.System_SByte => $"((sbyte){literal})",
            SpecialType.System_Byte => $"((byte){literal})",
            SpecialType.System_Int16 => $"((short){literal})",
            SpecialType.System_UInt16 => $"((ushort){literal})",
            _ => literal,
        };
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

    internal static string Quote(string value)
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
