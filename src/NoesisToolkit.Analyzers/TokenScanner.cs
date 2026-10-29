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
            var compatibility = new MarkupCompatibility();

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || !compatibility.Reads(reader))
                    continue;

                var depth = reader.Depth;
                while (scopes.Count > 0 && scopes[scopes.Count - 1].Depth >= depth)
                    scopes.RemoveAt(scopes.Count - 1);

                var elementName = reader.LocalName;
                var elementPrefix = reader.Prefix;
                var isSetter = elementName == "Setter";
                var isTargetHost = TargetTypeHosts.Contains(elementName);

                if (
                    elementName
                        is "DataTemplate"
                            or "HierarchicalDataTemplate"
                            or "ItemsPanelTemplate"
                    && !reader.IsEmptyElement
                )
                    scopes.Add(new TargetScope(depth, null, ""));

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

                    // A directive or a design-time value is no property of the element.
                    if (reader.NamespaceURI.Length > 0)
                        continue;

                    if (TryScanExtensions(reader, raw, lineInfo, references))
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

                    if (
                        !IsIdentifier(raw)
                        || BindingScanner.NamespaceOf(reader, elementPrefix) is not { } owner
                    )
                        continue;

                    references.Add(
                        new TokenReference(
                            lineInfo.LineNumber,
                            lineInfo.LinePosition,
                            owner,
                            elementName,
                            name,
                            raw
                        )
                    );
                }
                reader.MoveToElement();

                if (isSetter && property is not null && value is not null && IsIdentifier(value))
                    AddSetter(
                        reader,
                        references,
                        scopes,
                        property,
                        value,
                        hasTargetName,
                        lineInfo.LineNumber,
                        lineInfo.LinePosition
                    );

                if (isTargetHost && targetType is not null && !reader.IsEmptyElement)
                {
                    SplitQualified(targetType, out var prefix, out var typeName);
                    scopes.Add(
                        new TargetScope(
                            depth,
                            IsIdentifier(typeName)
                                ? BindingScanner.NamespaceOf(reader, prefix)
                                : null,
                            typeName
                        )
                    );
                }
            }
        }
        catch (XmlException)
        {
            return None;
        }

        return references;
    }

    static void AddSetter(
        XmlReader reader,
        List<TokenReference> references,
        List<TargetScope> scopes,
        string property,
        string value,
        bool hasTargetName,
        int line,
        int column
    )
    {
        string? ns;
        string typeName;
        string member;

        var dot = property.LastIndexOf('.');
        if (dot > 0)
        {
            SplitQualified(property.Substring(0, dot), out var prefix, out typeName);
            ns = IsIdentifier(typeName) ? BindingScanner.NamespaceOf(reader, prefix) : null;
            member = property.Substring(dot + 1);
        }
        else
        {
            if (hasTargetName || scopes.Count == 0)
                return;
            ns = scopes[scopes.Count - 1].Namespace;
            typeName = scopes[scopes.Count - 1].TypeName;
            member = property;
        }

        if (ns is null || !IsIdentifier(member))
            return;

        references.Add(new TokenReference(line, column, ns, typeName, member, value));
    }

    static bool TryScanExtensions(
        XmlReader reader,
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
        if (!IsIdentifier(name) || BindingScanner.NamespaceOf(reader, prefix) is not { } ns)
            return true;

        references.Add(
            new TokenReference(
                lineInfo.LineNumber,
                lineInfo.LinePosition,
                ns,
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
        public TargetScope(int depth, string? @namespace, string typeName)
        {
            Depth = depth;
            Namespace = @namespace;
            TypeName = typeName;
        }

        public int Depth { get; }

        public string? Namespace { get; }

        public string TypeName { get; }
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
        string @namespace,
        string typeName,
        string member,
        string value
    )
    {
        Line = line;
        Column = column;
        Namespace = @namespace;
        TypeName = typeName;
        Member = member;
        Value = value;
    }

    public int Line { get; }
    public int Column { get; }

    public string Namespace { get; }

    public string TypeName { get; }

    /// <summary>Empty for a markup extension, whose enum property the caller locates itself.</summary>
    public string Member { get; }

    public string Value { get; }

    public bool IsExtension => Member.Length == 0;
}
