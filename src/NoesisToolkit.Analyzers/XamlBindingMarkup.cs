using System;
using System.Collections.Generic;

namespace NoesisToolkit.CodeGen;

/// <summary>Reading a binding out of an attribute value. Only the scanner asks this, so it
/// lives beside it rather than in every assembly that parses XAML at all.</summary>
internal static class XamlBindingMarkup
{
    public static bool TryGetExtension(string value, string keyword, out string body)
    {
        body = "";
        if (string.IsNullOrEmpty(value) || value.StartsWith("{}", StringComparison.Ordinal))
            return false;

        var open = value.IndexOf("{" + keyword, StringComparison.Ordinal);
        if (open < 0)
            return false;

        var after = open + keyword.Length + 1;
        if (after < value.Length && value[after] != ' ' && value[after] != '}')
            return false;

        var depth = 0;
        for (var i = open; i < value.Length; i++)
        {
            if (value[i] == '{')
            {
                depth++;
            }
            else if (value[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    body = value.Substring(after, i - after).Trim();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The members of a binding this analyzer judges, off the shared markup parse.</summary>
    public static BindingExpression ParseBinding(string body) =>
        XamlMarkupParser.Parse("{Binding " + body + "}") is { } call
            ? Read(call)
            : new BindingExpression();

    /// <summary>The binding a whole attribute value is, or null where it is anything else, a
    /// binding nested in another extension included.</summary>
    public static BindingExpression? ParseValue(string value) =>
        XamlMarkupParser.Parse(value.Trim()) is { Name: "Binding" } call ? Read(call) : null;

    static BindingExpression Read(MarkupCall call)
    {
        var result = new BindingExpression();
        if (call.Positional.Count > 0)
            result.Path = call.Positional[0];

        result.PositionalCalls = call.PositionalCalls.Count > 0;
        foreach (var argument in call.Named)
        {
            // Raw rather than the parsed tree: RelativeSource is judged as the text it was written as.
            var text = argument.Value as string ?? ((MarkupCall)argument.Value).Raw;
            switch (argument.Key)
            {
                case "Path":
                    result.Path = text;
                    break;
                case "RelativeSource":
                    result.RelativeSource = text;
                    break;
                case "ElementName":
                    result.ElementName = text;
                    break;
                case "Source":
                    result.HasSource = true;
                    break;
                case "Mode":
                    result.WritesSourceOnly = text.Trim() == "OneWayToSource";
                    break;
                case "UpdateSourceTrigger":
                    break;
                case "Converter":
                case "StringFormat":
                    result.Converts = true;
                    result.ShapesValue = true;
                    break;
                default:
                    result.ShapesValue = true;
                    break;
            }
        }

        return result;
    }

    public static bool IsValidPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        foreach (var segment in path!.Split('.'))
        {
            if (!XamlMarkup.IsIdentifier(segment))
                return false;
        }
        return true;
    }
}

internal sealed class BindingExpression
{
    public string? Path;
    public string? RelativeSource;
    public string? ElementName;
    public bool HasSource;

    public bool PositionalCalls;

    public bool WritesSourceOnly;

    public bool ShapesValue;

    public bool Converts;

    // The rule the compiler types a redirected DataContext by.
    public bool RedirectsTyped =>
        RelativeSource is null
        && ElementName is null
        && !HasSource
        && !PositionalCalls
        && !WritesSourceOnly
        && !ShapesValue;

    public bool IsIdentity => Path is null || Path.Trim() is "" or ".";
}
