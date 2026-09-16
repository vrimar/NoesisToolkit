using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace NoesisToolkit.CodeGen;

/// <summary>
/// Extracts every place a XAML file names an enum member by string: a markup extension argument
/// such as <c>{ui:Brush X}</c>, an attribute written as a bare identifier, and a <c>Setter</c> whose
/// <c>Property</c> resolves to one. Each carries the type it is written against so the caller can
/// decide whether the member is enum-typed at all — a name like <c>Color</c> also reaches plain
/// non-enum properties, which must not be checked against an enum.
/// </summary>
internal static class TokenScanner
{
    static readonly TokenReference[] None = new TokenReference[0];

    /// <summary>Elements whose <c>TargetType</c> owns the unqualified <c>Property</c> of the
    /// <c>Setter</c>s beneath them.</summary>
    static readonly HashSet<string> TargetTypeHosts = new HashSet<string>(StringComparer.Ordinal)
    {
        "Style",
        "ControlTemplate",
    };

    public static IReadOnlyList<TokenReference> Scan(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return None;

        var references = new List<TokenReference>();
        var scopes = new List<TargetScope>();

        try
        {
            using var reader = CreateReader(content!);
            var lineInfo = (IXmlLineInfo)reader;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                var depth = reader.Depth;
                while (scopes.Count > 0 && scopes[scopes.Count - 1].Depth >= depth)
                    scopes.RemoveAt(scopes.Count - 1);

                var elementName = reader.LocalName;
                var elementPrefix = reader.Prefix;
                var isSetter = elementName == "Setter";
                var isTargetHost = TargetTypeHosts.Contains(elementName);

                if (!reader.HasAttributes)
                    continue;

                string? property = null;
                string? value = null;
                var hasTargetName = false;
                string? targetType = null;

                while (reader.MoveToNextAttribute())
                {
                    var name = reader.LocalName;
                    var raw = reader.Value;

                    if (TryScanExtensions(raw, lineInfo, references))
                        continue;

                    if (isTargetHost && name == "TargetType")
                        targetType = raw;

                    if (isSetter)
                    {
                        if (name == "Property")
                            property = raw;
                        else if (name == "Value")
                            value = raw;
                        else if (name == "TargetName")
                            hasTargetName = true;
                        continue;
                    }

                    if (!IsIdentifier(raw))
                        continue;

                    references.Add(
                        new TokenReference(
                            lineInfo.LineNumber,
                            lineInfo.LinePosition,
                            elementPrefix,
                            elementName,
                            name,
                            raw
                        )
                    );
                }
                reader.MoveToElement();

                if (isSetter && property is not null && value is not null && IsIdentifier(value))
                    AddSetter(
                        references,
                        scopes,
                        property,
                        value,
                        hasTargetName,
                        lineInfo.LineNumber,
                        lineInfo.LinePosition
                    );

                if (isTargetHost && targetType is not null && !reader.IsEmptyElement)
                    scopes.Add(new TargetScope(depth, targetType));
            }
        }
        catch (XmlException)
        {
            return None;
        }

        return references;
    }

    static void AddSetter(
        List<TokenReference> references,
        List<TargetScope> scopes,
        string property,
        string value,
        bool hasTargetName,
        int line,
        int column
    )
    {
        string owner;
        string member;

        var dot = property.LastIndexOf('.');
        if (dot > 0)
        {
            owner = property.Substring(0, dot);
            member = property.Substring(dot + 1);
        }
        else
        {
            if (hasTargetName || scopes.Count == 0)
                return;
            owner = scopes[scopes.Count - 1].TargetType;
            member = property;
        }

        if (!IsIdentifier(member))
            return;

        SplitQualified(owner, out var prefix, out var typeName);
        if (!IsIdentifier(typeName))
            return;

        references.Add(new TokenReference(line, column, prefix, typeName, member, value));
    }

    static bool TryScanExtensions(
        string raw,
        IXmlLineInfo lineInfo,
        List<TokenReference> references
    )
    {
        if (raw.Length == 0 || raw[0] != '{')
            return false;

        var close = raw.IndexOf('}');
        if (close < 0)
            return true;

        var body = raw.Substring(1, close - 1).Trim();
        var space = body.IndexOf(' ');
        if (space <= 0)
            return true;

        var keyword = body.Substring(0, space);
        var argument = body.Substring(space + 1).Trim();
        if (!IsIdentifier(argument))
            return true;

        SplitQualified(keyword, out var prefix, out var name);
        if (!IsIdentifier(name))
            return true;

        references.Add(
            new TokenReference(
                lineInfo.LineNumber,
                lineInfo.LinePosition,
                prefix,
                name + "Extension",
                "",
                argument
            )
        );
        return true;
    }

    static void SplitQualified(string qualified, out string prefix, out string name)
    {
        var colon = qualified.IndexOf(':');
        if (colon < 0)
        {
            prefix = "";
            name = qualified;
            return;
        }

        prefix = qualified.Substring(0, colon);
        name = qualified.Substring(colon + 1);
    }

    static bool IsIdentifier(string s)
    {
        if (s.Length == 0)
            return false;
        if (!char.IsLetter(s[0]) && s[0] != '_')
            return false;
        for (var i = 1; i < s.Length; i++)
        {
            if (!char.IsLetterOrDigit(s[i]) && s[i] != '_')
                return false;
        }
        return true;
    }

    static XmlReader CreateReader(string content) =>
        XmlReader.Create(
            new StringReader(content),
            new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true }
        );

    readonly struct TargetScope
    {
        public TargetScope(int depth, string targetType)
        {
            Depth = depth;
            TargetType = targetType;
        }

        public int Depth { get; }
        public string TargetType { get; }
    }
}

/// <summary>One place a XAML file names a member of an enum, as the type it is written against.
/// A markup extension carries an empty <see cref="Member"/> and its extension class in
/// <see cref="TypeName"/>; everything else names a property on a type.</summary>
internal readonly struct TokenReference
{
    public TokenReference(
        int line,
        int column,
        string prefix,
        string typeName,
        string member,
        string value
    )
    {
        Line = line;
        Column = column;
        Prefix = prefix;
        TypeName = typeName;
        Member = member;
        Value = value;
    }

    public int Line { get; }
    public int Column { get; }

    /// <summary>Empty when unprefixed, which means the presentation namespace.</summary>
    public string Prefix { get; }

    public string TypeName { get; }

    /// <summary>Empty for a markup extension, whose enum property the caller locates itself.</summary>
    public string Member { get; }

    public string Value { get; }

    public bool IsExtension => Member.Length == 0;
}
