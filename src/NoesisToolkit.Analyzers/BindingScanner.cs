using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace NoesisToolkit.CodeGen;

/// <summary>
/// Extracts every <c>{Binding}</c> a XAML file states enough about to check, as the type its
/// DataContext resolves to plus the member path. Anything whose context is unknowable — a keyed
/// template, a bare <c>Style</c>, an untyped template with no host — is left out rather than guessed.
/// </summary>
internal static class BindingScanner
{
    internal const string PresentationNamespace = "Noesis";

    internal const string ToolkitNamespace = XamlScopeRules.ToolkitNamespace;

    // Their Property=/RelativeSource=Self resolve against the styled element, never the XML element.
    static readonly HashSet<string> TriggerLike = new HashSet<string>(StringComparer.Ordinal)
    {
        "Setter",
        "Trigger",
        "MultiTrigger",
        "DataTrigger",
        "MultiDataTrigger",
        "EventTrigger",
        "Condition",
    };

    sealed class Walked
    {
        public readonly List<BindingCheck> Checks = new List<BindingCheck>();

        public readonly List<UnresolvedBinding> Unresolved = new List<UnresolvedBinding>();

        public bool CompileBindings;

        /// <summary>Whether the first name derives from the second, which only a compilation knows.</summary>
        public Func<string, string, bool>? Derives;

        /// <summary>Resource key to every site's scope; null once any one site cannot be resolved.</summary>
        public Dictionary<string, List<DataScope>?>? Keys;

        /// <summary>Sources written as property elements, by the ordinal of the element that owns
        /// them. A template can precede the source it reads, so the collect pass records these and
        /// every later pass reads them whatever the document order was.</summary>
        public Dictionary<int, Dictionary<string, string>> ElementSources =
            new Dictionary<int, Dictionary<string, string>>();
    }

    /// <summary>Both halves of the scan: the bindings whose DataContext is known, and the ones a
    /// document opting into compiled bindings still has to declare a type for.</summary>
    public static ScanResult ScanAll(string? content, Func<string, string, bool>? derives = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ScanResult.Empty;

        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var namedElements = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!CollectDeclarations(content!, prefixes, namedElements))
            return ScanResult.Empty;

        var nameToScope = new Dictionary<string, DataScope?>(StringComparer.Ordinal);
        var keyToScope = new Dictionary<string, List<DataScope>?>(StringComparer.Ordinal);
        var elementSources = new Dictionary<int, Dictionary<string, string>>();
        var discard = new Walked
        {
            Derives = derives,
            Keys = keyToScope,
            ElementSources = elementSources,
        };
        if (!Walk(content!, prefixes, namedElements, discard, nameToScope, true))
            return ScanResult.Empty;

        // A keyed template's scope is known only once the first pass has finished, so a name
        // declared inside one recorded a scope that pass could not see. Rebuild them now.
        nameToScope.Clear();
        var named = new Walked
        {
            Derives = derives,
            Keys = keyToScope,
            ElementSources = elementSources,
        };
        if (!Walk(content!, prefixes, namedElements, named, nameToScope, true))
            return ScanResult.Empty;

        var walked = new Walked
        {
            Derives = derives,
            Keys = keyToScope,
            ElementSources = elementSources,
        };
        return Walk(content!, prefixes, namedElements, walked, nameToScope, false)
            ? new ScanResult(walked.Checks, walked.Unresolved, walked.CompileBindings)
            : ScanResult.Empty;
    }

    /// <summary>Every <c>{x:Static}</c> one XAML file states, split into prefix, type and member.</summary>
    public static IReadOnlyList<StaticReference> ScanStaticReferences(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<StaticReference>();

        var references = new List<StaticReference>();
        var ok = ForEachAttribute(
            content!,
            (reader, lineInfo, _) =>
            {
                if (!XamlBindingMarkup.TryGetExtension(reader.Value, "x:Static", out var body))
                    return;

                var colon = body.IndexOf(':');
                var qualified = colon < 0 ? body : body.Substring(colon + 1);
                var dot = qualified.LastIndexOf('.');
                if (dot <= 0 || dot == qualified.Length - 1)
                    return;

                var type = qualified.Substring(0, dot);
                var member = qualified.Substring(dot + 1);
                if (!XamlMarkup.IsIdentifier(type) || !XamlMarkup.IsIdentifier(member))
                    return;

                references.Add(
                    new StaticReference(
                        lineInfo.LineNumber,
                        lineInfo.LinePosition,
                        colon < 0 ? "" : body.Substring(0, colon),
                        type,
                        member
                    )
                );
            }
        );

        return ok ? references : Array.Empty<StaticReference>();
    }

    /// <summary>Every <c>clr-namespace:</c> xmlns one XAML file declares, with its position.</summary>
    public static IReadOnlyList<XmlnsDeclaration> ScanNamespaces(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<XmlnsDeclaration>();

        var declarations = new List<XmlnsDeclaration>();
        var ok = ForEachAttribute(
            content!,
            (reader, lineInfo, _) =>
            {
                if (
                    reader.Prefix != "xmlns"
                    || !reader.Value.StartsWith(ClrNamespace, StringComparison.Ordinal)
                )
                    return;

                declarations.Add(
                    new XmlnsDeclaration(
                        lineInfo.LineNumber,
                        lineInfo.LinePosition,
                        reader.LocalName,
                        ClrNamespaceOf(reader.Value)
                    )
                );
            }
        );

        return ok ? declarations : Array.Empty<XmlnsDeclaration>();
    }

    /// <summary>Visits every attribute of every element. Returns false when the document is not
    /// well-formed, which a scanner treats as nothing to say rather than as an error.</summary>
    static bool ForEachAttribute(string content, Action<XmlReader, IXmlLineInfo, string> visit)
    {
        try
        {
            using var reader = CreateReader(content);
            var lineInfo = (IXmlLineInfo)reader;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || !reader.HasAttributes)
                    continue;

                // Read before the loop moves the reader onto an attribute, where it names that.
                var tag = QualifiedTag(reader);
                while (reader.MoveToNextAttribute())
                    visit(reader, lineInfo, tag);

                reader.MoveToElement();
            }

            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    const string ClrNamespace = "clr-namespace:";

    // The assembly= suffix is not part of the namespace name.
    static string ClrNamespaceOf(string value)
    {
        var body = value.Substring(ClrNamespace.Length);
        var semi = body.IndexOf(';');
        return semi < 0 ? body : body.Substring(0, semi);
    }

    static bool CollectDeclarations(
        string content,
        Dictionary<string, string> prefixes,
        Dictionary<string, string> namedElements
    ) =>
        ForEachAttribute(
            content,
            (reader, _, tag) =>
            {
                if (
                    reader.Prefix == "xmlns"
                    && reader.Value.StartsWith(ClrNamespace, StringComparison.Ordinal)
                )
                {
                    prefixes[reader.LocalName] = ClrNamespaceOf(reader.Value);
                }
                else if (reader.Name == "x:Name" || reader.Name == "Name")
                {
                    // Each template is its own name scope, so a name can repeat within a file.
                    namedElements[reader.Value] =
                        namedElements.TryGetValue(reader.Value, out var seen) && seen != tag
                            ? ""
                            : tag;
                }
            }
        );

    static bool Walk(
        string content,
        Dictionary<string, string> prefixes,
        Dictionary<string, string> namedElements,
        Walked walked,
        Dictionary<string, DataScope?> nameToScope,
        bool collect
    )
    {
        var stack = new List<Frame> { default };
        var ordinal = 0;

        try
        {
            using var reader = CreateReader(content);
            var lineInfo = (IXmlLineInfo)reader;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (stack.Count > 1)
                        stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                var isEmpty = reader.IsEmptyElement;
                var parent = stack[stack.Count - 1];
                var frame = BuildFrame(
                    reader,
                    parent,
                    stack,
                    prefixes,
                    namedElements,
                    walked,
                    ordinal++
                );

                if (collect && stack.Count >= 2)
                    RecordElementSource(reader, parent, stack[stack.Count - 2], walked);

                if (walked.ElementSources.TryGetValue(frame.Ordinal, out var written))
                {
                    frame.Sources ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var pair in written)
                        frame.Sources[pair.Key] = pair.Value;
                }

                if (reader.GetAttribute("CompileBindings", ToolkitNamespace) is { } opt)
                {
                    frame.Compile = opt.Trim() != "False";
                    walked.CompileBindings |= frame.Compile;
                }

                if (collect)
                {
                    var named = reader.GetAttribute("x:Name") ?? reader.GetAttribute("Name");
                    if (named is not null)
                    {
                        nameToScope[named] = nameToScope.ContainsKey(named) ? null : frame.Data;
                    }
                }

                ProcessAttributes(
                    reader,
                    lineInfo,
                    ref frame,
                    stack,
                    prefixes,
                    namedElements,
                    nameToScope,
                    collect,
                    walked
                );

                if (!isEmpty)
                    stack.Add(frame);
            }
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    static Frame BuildFrame(
        XmlReader reader,
        Frame parent,
        List<Frame> stack,
        Dictionary<string, string> prefixes,
        Dictionary<string, string> namedElements,
        Walked walked,
        int ordinal
    )
    {
        var name = reader.LocalName;
        var triggerLike = TriggerLike.Contains(name);

        var frame = new Frame
        {
            Ordinal = ordinal,
            Data = parent.Data,
            ParentData = parent.Data,
            Alternates = parent.Alternates,
            Compile = parent.Compile,
            TargetType = parent.TargetType,
            SelfType = triggerLike
                ? parent.TargetType
                : ResolveTag(reader.Prefix, reader.LocalName, prefixes),
        };

        if (triggerLike)
        {
            var redirect = reader.GetAttribute("TargetName") ?? reader.GetAttribute("SourceName");
            frame.PropertyOwner =
                redirect is null ? parent.TargetType
                : namedElements.TryGetValue(redirect, out var tag) && tag.Length > 0
                    ? ResolveQualified(tag, prefixes)
                : null;
        }

        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            var propertyName = name.Substring(dot + 1);
            frame.PropertyName = propertyName;
            frame.Transparent = XamlScopeRules.TransparentHosts.Contains(propertyName);

            if (XamlScopeRules.TemplateHosts.TryGetValue(propertyName, out var host))
                frame.PendingHost = HostedScope(stack, host);

            return frame;
        }

        frame.Breaks = XamlScopeRules.ScopeBreakers.Contains(name);

        switch (name)
        {
            case "DataTemplate":
                frame.Data = ResolveDataTemplateScope(
                    reader,
                    parent,
                    prefixes,
                    walked,
                    out frame.Alternates
                );
                break;
            case "Style":
            case "ControlTemplate":
                frame.Data = TemplatedScope(reader, parent, walked, out frame.Alternates);
                frame.TargetType =
                    ResolveTypeAttribute(reader, "TargetType", prefixes) ?? parent.TargetType;
                break;
            case "ItemsPanelTemplate":
                frame.Data = TemplatedScope(reader, parent, walked, out frame.Alternates);
                break;
        }

        if (reader.GetAttribute("DataType", ToolkitNamespace) is { } annotated)
        {
            frame.Alternates = null;
            frame.Data = ResolveTypeReference(annotated, prefixes) is { } resolved
                ? new DataScope(resolved)
                : null;
        }

        if (reader.GetAttribute("DataContext") is { } explicitContext)
        {
            frame.Alternates = null;
            var binding = XamlBindingMarkup.TryGetExtension(
                explicitContext,
                "Binding",
                out var explicitBody
            )
                ? XamlBindingMarkup.ParseBinding(explicitBody)
                : null;
            frame.Data =
                binding is not null
                && binding.IsPlainDataContext
                && frame.Data is not null
                && XamlBindingMarkup.IsValidPath(binding.Path)
                    ? frame.Data.Extend(binding.Path!, false)
                    : null;
        }

        return frame;
    }

    static DataScope? ResolveDataTemplateScope(
        XmlReader reader,
        Frame parent,
        Dictionary<string, string> prefixes,
        Walked walked,
        out List<DataScope>? alternates
    )
    {
        alternates = null;
        if (ResolveTypeAttribute(reader, "DataType", prefixes) is { } declared)
            return new DataScope(declared);

        if (reader.GetAttribute("x:Key") is not { } key)
            return parent.PendingHost;

        alternates = KeyedScope(key, walked);
        return alternates is { Count: > 0 } ? alternates[0] : null;
    }

    static DataScope? TemplatedScope(
        XmlReader reader,
        Frame parent,
        Walked walked,
        out List<DataScope>? alternates
    )
    {
        alternates = null;
        if (reader.GetAttribute("x:Key") is not { } key)
            return parent.Transparent ? parent.Data : parent.PendingHost;

        alternates = KeyedScope(key, walked);
        return alternates is { Count: > 0 } ? alternates[0] : null;
    }

    static List<DataScope>? KeyedScope(string key, Walked walked) =>
        walked.Keys is not null && walked.Keys.TryGetValue(key.Trim(), out var scopes)
            ? scopes
            : null;

    // A cell template sits under the column rather than the list, so the source is the nearest one
    // named above -- but never past a breaker, which renders something else entirely.
    static DataScope? HostedScope(List<Frame> stack, XamlScopeRules.TemplateHost host)
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            var frame = stack[i];
            if (
                frame.Sources is not null
                && frame.Sources.TryGetValue(host.SourceProperty, out var path)
            )
            {
                // A pathless source hands on the DataContext itself, unchanged.
                return path.Length == 0 && !host.IsCollection
                    ? frame.Data
                    : frame.Data?.Extend(path, host.IsCollection);
            }

            if (frame.Breaks)
                return null;
        }

        return null;
    }

    static void ProcessAttributes(
        XmlReader reader,
        IXmlLineInfo lineInfo,
        ref Frame frame,
        List<Frame> stack,
        Dictionary<string, string> prefixes,
        Dictionary<string, string> namedElements,
        Dictionary<string, DataScope?> nameToScope,
        bool collect,
        Walked walked
    )
    {
        if (!reader.HasAttributes)
            return;

        var triggerLike = TriggerLike.Contains(reader.LocalName);
        Dictionary<string, string>? sources = null;
        List<KeyUse>? keys = null;

        while (reader.MoveToNextAttribute())
        {
            if (reader.Name.StartsWith("xmlns", StringComparison.Ordinal))
                continue;

            var value = reader.Value;
            var line = lineInfo.LineNumber;
            var column = lineInfo.LinePosition;

            if (
                XamlBindingMarkup.TryGetExtension(value, "Binding", out var hostBody)
                && IsSourceProperty(reader.LocalName)
            )
            {
                var hostBinding = XamlBindingMarkup.ParseBinding(hostBody);
                if (
                    hostBinding.IsPlainDataContext
                    && (
                        string.IsNullOrEmpty(hostBinding.Path)
                        || XamlBindingMarkup.IsValidPath(hostBinding.Path)
                    )
                )
                {
                    sources ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    sources[reader.LocalName] = hostBinding.Path ?? "";
                }
            }

            if (
                XamlScopeRules.TemplateHosts.ContainsKey(reader.LocalName)
                && XamlBindingMarkup.TryGetExtension(value, "StaticResource", out var keyed)
            )
            {
                keys ??= new List<KeyUse>();
                keys.Add(new KeyUse(reader.LocalName, keyed.Trim()));
            }

            if (collect)
                continue;

            // GridViewColumn.DisplayMemberBinding binds against the row, not the ambient DataContext.
            if (reader.LocalName == "DisplayMemberBinding")
                continue;

            if (triggerLike && reader.LocalName == "Property")
            {
                if (frame.PropertyOwner is not null && XamlMarkup.IsIdentifier(value))
                    walked.Checks.Add(
                        new BindingCheck(line, column, new DataScope(frame.PropertyOwner), value)
                    );
                continue;
            }

            if (XamlBindingMarkup.TryGetExtension(value, "TemplateBinding", out var templatePath))
            {
                if (frame.TargetType is not null && XamlMarkup.IsIdentifier(templatePath))
                    walked.Checks.Add(
                        new BindingCheck(
                            line,
                            column,
                            new DataScope(frame.TargetType),
                            templatePath
                        )
                    );
                continue;
            }

            if (!XamlBindingMarkup.TryGetExtension(value, "Binding", out var body))
                continue;

            EmitCheck(
                XamlBindingMarkup.ParseBinding(body),
                line,
                column,
                frame,
                stack,
                reader.LocalName == "DataContext",
                prefixes,
                namedElements,
                nameToScope,
                walked
            );
        }

        reader.MoveToElement();

        if (sources is not null)
        {
            // Merged, not assigned: a property-element source was already recorded on this frame.
            frame.Sources ??= new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in sources)
                frame.Sources[pair.Key] = pair.Value;
        }

        if (!collect || keys is null || walked.Keys is null)
            return;

        stack.Add(frame);
        foreach (var use in keys)
        {
            var known = walked.Keys.TryGetValue(use.Key, out var seen);
            if (known && seen is null)
                continue;

            var scope = HostedScope(stack, XamlScopeRules.TemplateHosts[use.Property]);
            if (scope is null)
            {
                walked.Keys[use.Key] = null;
                continue;
            }

            if (!known)
                walked.Keys[use.Key] = seen = new List<DataScope>();

            if (!seen!.Exists(s => s.Matches(scope)))
                seen.Add(scope);
        }
        stack.RemoveAt(stack.Count - 1);
    }

    readonly struct KeyUse(string property, string key)
    {
        public string Property { get; } = property;
        public string Key { get; } = key;
    }

    static void EmitCheck(
        BindingExpression binding,
        int line,
        int column,
        Frame frame,
        List<Frame> stack,
        bool isDataContext,
        Dictionary<string, string> prefixes,
        Dictionary<string, string> namedElements,
        Dictionary<string, DataScope?> nameToScope,
        Walked walked
    )
    {
        if (binding.HasSource || binding.Path is null)
            return;

        var raw = binding.Path;
        if (!XamlBindingMarkup.IsValidPath(raw))
            return;

        var path = raw;

        if (binding.RelativeSource is not null)
        {
            var relative = binding.RelativeSource;
            var mode = RelativeSourceMode(relative);
            if (mode == "TemplatedParent")
            {
                if (frame.TargetType is not null)
                    walked.Checks.Add(
                        new BindingCheck(line, column, new DataScope(frame.TargetType), path)
                    );
            }
            else if (mode == "Self")
            {
                if (frame.SelfType is not null)
                    walked.Checks.Add(
                        new BindingCheck(line, column, new DataScope(frame.SelfType), path)
                    );
            }
            else if (TrySplitDataContextHop(raw, out var ancestorTail))
            {
                if (
                    AncestorTypeName(relative, prefixes) is { } target
                    && AncestorScope(stack, target, walked.Derives) is { } ancestorScope
                )
                    walked.Checks.Add(new BindingCheck(line, column, ancestorScope, ancestorTail));
            }
            else if (AncestorTypeName(relative, prefixes) is { } ancestor)
            {
                walked.Checks.Add(new BindingCheck(line, column, new DataScope(ancestor), path));
            }
            return;
        }

        if (binding.ElementName is not null)
        {
            if (TrySplitDataContextHop(raw, out var elementTail))
            {
                if (
                    nameToScope.TryGetValue(binding.ElementName, out var namedScope)
                    && namedScope is not null
                )
                    walked.Checks.Add(new BindingCheck(line, column, namedScope, elementTail));
            }
            else if (
                namedElements.TryGetValue(binding.ElementName, out var tag)
                && tag.Length > 0
                && ResolveQualified(tag, prefixes) is { } elementType
            )
            {
                walked.Checks.Add(new BindingCheck(line, column, new DataScope(elementType), path));
            }
            return;
        }

        var scope = isDataContext ? frame.ParentData : frame.Data;
        if (scope is not null)
        {
            if (!isDataContext && frame.Alternates is { Count: > 1 } every)
            {
                foreach (var alternate in every)
                    walked.Checks.Add(new BindingCheck(line, column, alternate, path));
                return;
            }

            walked.Checks.Add(new BindingCheck(line, column, scope, path));
            return;
        }

        if (frame.Compile)
            walked.Unresolved.Add(new UnresolvedBinding(line, column, path));
    }

    // Safe only because the walk knows the target element's DataContext; otherwise this hop is object.
    static bool TrySplitDataContextHop(string path, out string tail)
    {
        tail = "";
        const string prefix = "DataContext.";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var rest = path.Substring(prefix.Length);
        if (!XamlBindingMarkup.IsValidPath(rest))
            return false;

        tail = rest;
        return true;
    }

    // AncestorType=ItemsControl has to match a ListBox, which only the compilation can answer.
    static DataScope? AncestorScope(
        List<Frame> stack,
        string targetType,
        Func<string, string, bool>? derives
    )
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].SelfType is not { } self)
                continue;

            if (self == targetType || derives?.Invoke(self, targetType) == true)
                return stack[i].Data;
        }
        return null;
    }

    static bool IsSourceProperty(string name) => name is "Content" or "ItemsSource" or "Header";

    /// <summary>A source written as &lt;Owner.ItemsSource&gt;&lt;Binding/&gt;, which the attribute
    /// loop never sees. Recorded against the element that owns the property element.</summary>
    static void RecordElementSource(XmlReader reader, Frame parent, Frame owner, Walked walked)
    {
        if (
            reader.LocalName != "Binding"
            || parent.PropertyName is not { } property
            || !IsSourceProperty(property)
        )
            return;

        // Anything that redirects the source is not the ambient DataContext this hands on.
        if (
            reader.GetAttribute("Source") is not null
            || reader.GetAttribute("RelativeSource") is not null
            || reader.GetAttribute("ElementName") is not null
        )
            return;

        var path = (reader.GetAttribute("Path") ?? "").Trim();
        if (path.Length > 0 && !XamlBindingMarkup.IsValidPath(path))
            return;

        if (!walked.ElementSources.TryGetValue(owner.Ordinal, out var sources))
            walked.ElementSources[owner.Ordinal] = sources = new Dictionary<string, string>(
                StringComparer.Ordinal
            );

        sources[property] = path;
    }

    // Scraping the text would match an AncestorType whose own name contains one of these words.
    static string? RelativeSourceMode(string relativeSource)
    {
        if (XamlMarkupParser.Parse(relativeSource) is not { } call)
            return null;

        if (call.Positional.Count > 0)
            return call.Positional[0].Trim();

        foreach (var argument in call.Named)
        {
            if (argument.Key == "Mode" && argument.Value is string mode)
                return mode.Trim();
        }

        return null;
    }

    // Re-parsed rather than scraped: AncestorType is as often {x:Type ui:Thing} as a bare name.
    static string? AncestorTypeName(string relativeSource, Dictionary<string, string> prefixes)
    {
        if (XamlMarkupParser.Parse(relativeSource) is not { } call)
            return null;

        foreach (var argument in call.Named)
        {
            if (argument.Key != "AncestorType")
                continue;

            var reference =
                argument.Value as string
                ?? (
                    argument.Value is MarkupCall nested && nested.Positional.Count > 0
                        ? nested.Positional[0]
                        : null
                );

            return reference is null ? null : ResolveTypeReference(reference, prefixes);
        }

        return null;
    }

    static string? ResolveTypeAttribute(
        XmlReader reader,
        string attribute,
        Dictionary<string, string> prefixes
    ) => reader.GetAttribute(attribute) is { } raw ? ResolveTypeReference(raw, prefixes) : null;

    static string? ResolveTypeReference(string raw, Dictionary<string, string> prefixes)
    {
        var value = raw.Trim();
        if (XamlBindingMarkup.TryGetExtension(value, "x:Type", out var inner))
            value = inner.Trim();

        return value.IndexOf('{') >= 0 ? null : ResolveQualified(value, prefixes);
    }

    static string? ResolveQualified(string qualified, Dictionary<string, string> prefixes)
    {
        var colon = qualified.IndexOf(':');
        if (colon < 0)
        {
            return XamlMarkup.IsIdentifier(qualified)
                ? PresentationNamespace + "." + qualified
                : null;
        }

        var prefix = qualified.Substring(0, colon);
        var name = qualified.Substring(colon + 1);
        if (!XamlMarkup.IsIdentifier(name) || !prefixes.TryGetValue(prefix, out var ns))
            return null;

        return ns + "." + name;
    }

    static string? ResolveTag(string prefix, string localName, Dictionary<string, string> prefixes)
    {
        if (localName.IndexOf('.') >= 0)
            return null;
        return ResolveQualified(
            prefix.Length == 0 ? localName : prefix + ":" + localName,
            prefixes
        );
    }

    static string QualifiedTag(XmlReader reader) =>
        reader.Prefix.Length == 0 ? reader.LocalName : reader.Prefix + ":" + reader.LocalName;

    static XmlReader CreateReader(string content) =>
        XmlReader.Create(
            new StringReader(content),
            new XmlReaderSettings { IgnoreComments = false, IgnoreWhitespace = true }
        );

    struct Frame
    {
        public DataScope? Data;
        public DataScope? ParentData;
        public string? TargetType;
        public string? SelfType;
        public string? PropertyOwner;
        public DataScope? PendingHost;
        public Dictionary<string, string>? Sources;
        public int Ordinal;
        public string? PropertyName;
        public bool Transparent;
        public bool Breaks;
        public bool Compile;

        /// <summary>Every scope a keyed template is used from; the path has to resolve against all.</summary>
        public List<DataScope>? Alternates;
    }
}

/// <summary>A context type plus the property hops from it to the DataContext a binding actually sees.</summary>
internal sealed class DataScope
{
    public DataScope(string rootType)
        : this(rootType, Array.Empty<Hop>()) { }

    DataScope(string rootType, Hop[] hops)
    {
        RootType = rootType;
        Hops = hops;
    }

    /// <summary>Metadata name of the type the DataContext resolves to.</summary>
    public string RootType { get; }

    public Hop[] Hops { get; }

    public bool Matches(DataScope? other)
    {
        if (other is null || other.RootType != RootType || other.Hops.Length != Hops.Length)
            return false;

        for (var i = 0; i < Hops.Length; i++)
        {
            if (
                Hops[i].Path != other.Hops[i].Path
                || Hops[i].IsCollection != other.Hops[i].IsCollection
            )
                return false;
        }

        return true;
    }

    public DataScope Extend(string path, bool isCollection)
    {
        var hops = new Hop[Hops.Length + 1];
        Array.Copy(Hops, hops, Hops.Length);
        hops[Hops.Length] = new Hop(path, isCollection);
        return new DataScope(RootType, hops);
    }

    public readonly struct Hop
    {
        public Hop(string path, bool isCollection)
        {
            Path = path;
            IsCollection = isCollection;
        }

        public string Path { get; }

        /// <summary>The hop lands on a collection; the DataContext is its element type.</summary>
        public bool IsCollection { get; }
    }
}

/// <summary>One <c>{x:Static prefix:Type.Member}</c> reference and where it sits in the file.</summary>
internal readonly struct StaticReference
{
    public StaticReference(int line, int column, string prefix, string typeName, string memberName)
    {
        Line = line;
        Column = column;
        Prefix = prefix;
        TypeName = typeName;
        MemberName = memberName;
    }

    public int Line { get; }
    public int Column { get; }

    /// <summary>Empty when the reference is unprefixed, which means the presentation namespace.</summary>
    public string Prefix { get; }

    public string TypeName { get; }
    public string MemberName { get; }
}

/// <summary>One <c>xmlns:prefix="clr-namespace:N"</c> declaration and where it sits in the file.</summary>
internal readonly struct XmlnsDeclaration
{
    public XmlnsDeclaration(int line, int column, string prefix, string @namespace)
    {
        Line = line;
        Column = column;
        Prefix = prefix;
        Namespace = @namespace;
    }

    public int Line { get; }
    public int Column { get; }
    public string Prefix { get; }
    public string Namespace { get; }
}

/// <summary>A binding whose DataContext type the file does not state, so nothing can check it.</summary>
internal readonly struct UnresolvedBinding
{
    public UnresolvedBinding(int line, int column, string path)
    {
        Line = line;
        Column = column;
        Path = path;
    }

    public int Line { get; }
    public int Column { get; }
    public string Path { get; }
}

/// <summary>What one scan found, and whether the document asked for its bindings to be compiled.</summary>
internal sealed class ScanResult
{
    internal static readonly ScanResult Empty = new(
        Array.Empty<BindingCheck>(),
        Array.Empty<UnresolvedBinding>(),
        false
    );

    internal ScanResult(
        IReadOnlyList<BindingCheck> checks,
        IReadOnlyList<UnresolvedBinding> unresolved,
        bool compileBindings
    )
    {
        Checks = checks;
        Unresolved = unresolved;
        CompileBindings = compileBindings;
    }

    public IReadOnlyList<BindingCheck> Checks { get; }

    public IReadOnlyList<UnresolvedBinding> Unresolved { get; }

    /// <summary>The document carries an <c>ntk:CompileBindings</c> marker, which makes a declared
    /// DataContext type mandatory: an unresolved binding is an error rather than a silent skip.</summary>
    public bool CompileBindings { get; }
}

/// <summary>One checkable binding: where its DataContext comes from, and the member path off it.</summary>
internal readonly struct BindingCheck
{
    public BindingCheck(int line, int column, DataScope scope, string path)
    {
        Line = line;
        Column = column;
        ContextType = scope.RootType;
        Hops = scope.Hops;
        Path = path;
    }

    public int Line { get; }
    public int Column { get; }
    public string ContextType { get; }
    public DataScope.Hop[] Hops { get; }
    public string Path { get; }
}
