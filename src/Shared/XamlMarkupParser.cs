using System;
using System.Collections.Generic;
using System.Text;

namespace NoesisToolkit.CodeGen;

internal sealed class MarkupCall
{
    public string Name = "";

    /// <summary>The exact source text of this call, so a caller that wants the argument as written
    /// does not have to render the tree back.</summary>
    public string Raw = "";

    public readonly List<string> Positional = new List<string>();
    public readonly List<MarkupCall> PositionalCalls = new List<MarkupCall>();
    public readonly List<KeyValuePair<string, object>> Named =
        new List<KeyValuePair<string, object>>();
}

/// <summary>
/// Parses a XAML markup extension body — <c>{Binding Path, Mode=TwoWay,
/// RelativeSource={RelativeSource AncestorType=ItemsControl}}</c> — into a name, positional
/// arguments, and named arguments whose values are either a <see cref="string"/> or a nested
/// <see cref="MarkupCall"/>. Handles single-quoted values and the <c>{}</c> literal-brace escape.
/// </summary>
internal static class XamlMarkupParser
{
    public static bool IsMarkup(string value) =>
        value.Length > 1 && value[0] == '{' && value[value.Length - 1] == '}' && !IsEscape(value);

    public static bool IsEscape(string value) =>
        value.Length >= 2 && value[0] == '{' && value[1] == '}';

    public static string Unescape(string value) => IsEscape(value) ? value.Substring(2) : value;

    public static MarkupCall? Parse(string value)
    {
        if (!IsMarkup(value))
            return null;

        var pos = 0;
        var body = value.Substring(1, value.Length - 2);
        var call = ParseBody(body, ref pos);
        return pos >= body.Length ? call : null;
    }

    static MarkupCall? ParseBody(string s, ref int i)
    {
        var open = i;
        SkipWs(s, ref i);
        var name = ReadUntil(s, ref i, c => char.IsWhiteSpace(c) || c == ',' || c == '}');
        if (name.Length == 0)
            return null;

        var call = new MarkupCall { Name = name.Trim() };

        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] == '}')
                break;

            if (s[i] == ',')
            {
                i++;
                continue;
            }

            var start = i;
            var token = ReadUntil(s, ref i, c => c == '=' || c == ',' || c == '}');

            if (i < s.Length && s[i] == '=')
            {
                i++;
                SkipWs(s, ref i);
                var val = ReadValue(s, ref i);
                call.Named.Add(new KeyValuePair<string, object>(token.Trim(), val));
            }
            else
            {
                i = start;
                var val = ReadValue(s, ref i);
                if (val is string text)
                    call.Positional.Add(text);
                else if (val is MarkupCall nested)
                    call.PositionalCalls.Add(nested);
                else
                    return null;
            }
        }

        if (i < s.Length && s[i] == '}')
            i++;

        call.Raw = "{" + s.Substring(open, i - open).TrimEnd('}') + "}";
        return call;
    }

    static object ReadValue(string s, ref int i)
    {
        SkipWs(s, ref i);

        if (i < s.Length && s[i] == '{')
        {
            if (i + 1 < s.Length && s[i + 1] == '}')
            {
                i += 2;
                return ReadArgument(s, ref i);
            }

            i++;
            var nested = ParseBody(s, ref i);
            return (object?)nested ?? "";
        }

        if (i < s.Length && (s[i] == '\'' || s[i] == '"'))
        {
            var quote = s[i++];
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != quote)
                sb.Append(s[i++]);
            if (i < s.Length)
                i++;
            var text = sb.ToString();
            return text.StartsWith("{}", StringComparison.Ordinal) ? text.Substring(2) : text;
        }

        return ReadArgument(s, ref i);
    }

    static string ReadUntil(string s, ref int i, Func<char, bool> stop)
    {
        var sb = new StringBuilder();
        while (i < s.Length && !stop(s[i]))
            sb.Append(s[i++]);
        return sb.ToString();
    }

    // A literal may carry balanced braces of its own, so only the closing brace ends the argument.
    static string ReadArgument(string s, ref int i)
    {
        var sb = new StringBuilder();
        var depth = 0;

        while (i < s.Length)
        {
            var c = s[i];
            if (depth == 0 && (c == ',' || c == '}'))
                break;

            if (c == '{')
                depth++;
            else if (c == '}')
                depth--;

            sb.Append(c);
            i++;
        }

        return sb.ToString().TrimEnd();
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }
}
