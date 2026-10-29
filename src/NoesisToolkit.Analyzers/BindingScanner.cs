using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace NoesisToolkit.CodeGen;

/// <summary>
/// Extracts every <c>{Binding}</c> a XAML file states enough about to check, as the type its
/// DataContext resolves to plus the member path. Anything whose context is unknowable — a keyed
/// template with no <c>ntk:DataType</c>, a bare <c>Style</c>, an untyped template with no host — is
/// left out rather than guessed.
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

        /// <summary>Sources written as property elements, by the ordinal of the element that owns
        /// them. A template can precede the source it reads, so the collect pass records these and
        /// every later pass reads them whatever the document order was.</summary>
        public Dictionary<int, Dictionary<string, string?>> ElementSources =
            new Dictionary<int, Dictionary<string, string?>>();

        public Dictionary<int, HashSet<string>> PropertyElements =
            new Dictionary<int, HashSet<string>>();

        public StyleFacts Styles = new StyleFacts();

        // Read off a later element than the one it rebinds, so it is collected ahead too.
        public HashSet<int> Rebound = new HashSet<int>();
    }

    sealed class StyleFacts
    {
        public readonly Dictionary<string, HashSet<int>> Keyed = new Dictionary<
            string,
            HashSet<int>
        >(StringComparer.Ordinal);

        public readonly Dictionary<int, string> BasedOn = new Dictionary<int, string>();

        public readonly HashSet<int> SetDataContext = new HashSet<int>();

        public readonly Dictionary<int, HashSet<int>> Inline = new Dictionary<int, HashSet<int>>();

        public readonly Dictionary<int, HashSet<string>> Referenced =
            new Dictionary<int, HashSet<string>>();

        public bool Rebinds(int element)
        {
            var seen = new HashSet<int>();
            return Inline.TryGetValue(element, out var inline)
                    && inline.Any(style => SetsDataContext(style, seen))
                || Referenced.TryGetValue(element, out var keys)
                    && keys.Any(key => KeySetsDataContext(key, seen));
        }

        bool SetsDataContext(int style, HashSet<int> seen) =>
            seen.Add(style)
            && (
                SetDataContext.Contains(style)
                || BasedOn.TryGetValue(style, out var key) && KeySetsDataContext(key, seen)
            );

        bool KeySetsDataContext(string key, HashSet<int> seen) =>
            Keyed.TryGetValue(key, out var styles)
            && styles.Any(style => SetsDataContext(style, seen));
    }

    /// <summary>Both halves of the scan: the bindings whose DataContext is known, and the ones a
    /// document opting into compiled bindings still has to declare a type for.</summary>
    public static ScanResult ScanAll(string? content, Func<string, string, bool>? derives = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return ScanResult.Empty;

        var namedElements = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!CollectDeclarations(content!, namedElements))
            return ScanResult.Empty;

        var nameToScope = new Dictionary<string, DataScope?>(StringComparer.Ordinal);
        var discard = new Walked { Derives = derives };
        if (!Walk(content!, namedElements, discard, nameToScope, true))
            return ScanResult.Empty;

        Settle(discard);

        // A template can precede the property element that sources it, so a name declared inside
        // one recorded a scope the first pass could not see. Rebuild them now.
        nameToScope.Clear();
        var named = Again(discard);
        if (!Walk(content!, namedElements, named, nameToScope, true))
            return ScanResult.Empty;

        var walked = Again(discard);
        return Walk(content!, namedElements, walked, nameToScope, false)
            ? new ScanResult(walked.Checks, walked.Unresolved, walked.CompileBindings)
            : ScanResult.Empty;
    }

    static Walked Again(Walked collected) =>
        new Walked
        {
            Derives = collected.Derives,
            ElementSources = collected.ElementSources,
            PropertyElements = collected.PropertyElements,
            Styles = collected.Styles,
            Rebound = collected.Rebound,
        };

    static void Settle(Walked walked)
    {
        foreach (var pair in walked.PropertyElements)
        {
            if (pair.Value.Contains("DataContext"))
                walked.Rebound.Add(pair.Key);
        }

        foreach (var element in walked.Styles.Inline.Keys.Concat(walked.Styles.Referenced.Keys))
        {
            if (walked.Styles.Rebinds(element))
                walked.Rebound.Add(element);
        }
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

                if (NamespaceOf(reader, colon < 0 ? "" : body.Substring(0, colon)) is not { } ns)
                    return;

                references.Add(
                    new StaticReference(
                        lineInfo.LineNumber,
                        lineInfo.LinePosition,
                        ns,
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
        try
        {
            using var reader = CreateReader(content!);
            var lineInfo = (IXmlLineInfo)reader;
            var compatibility = new MarkupCompatibility();
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || !compatibility.Reads(reader))
                    continue;

                while (reader.MoveToNextAttribute())
                {
                    if (
                        reader.Prefix != "xmlns"
                        || !reader.Value.StartsWith(ClrNamespace, StringComparison.Ordinal)
                        || compatibility.Drops(reader.Value)
                        || ClrNamespaceOf(reader.Value) is not { } ns
                    )
                        continue;

                    declarations.Add(
                        new XmlnsDeclaration(
                            lineInfo.LineNumber,
                            lineInfo.LinePosition,
                            reader.LocalName,
                            ns
                        )
                    );
                }

                reader.MoveToElement();
            }
        }
        catch (XmlException)
        {
            return Array.Empty<XmlnsDeclaration>();
        }

        return declarations;
    }

    /// <summary>Every element one XAML file names through a <c>clr-namespace:</c> prefix, by the type
    /// it names: its owner, for a property element.</summary>
    public static IReadOnlyList<ElementTypeReference> ScanElementTypes(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<ElementTypeReference>();

        var references = new List<ElementTypeReference>();
        try
        {
            using var reader = CreateReader(content!);
            var lineInfo = (IXmlLineInfo)reader;
            var compatibility = new MarkupCompatibility();
            while (reader.Read())
            {
                if (
                    reader.NodeType != XmlNodeType.Element
                    || !compatibility.Reads(reader)
                    || NamespaceOf(reader, reader.Prefix) is not { } ns
                    || ns == PresentationNamespace
                )
                    continue;

                var dot = reader.LocalName.IndexOf('.');
                references.Add(
                    new ElementTypeReference(
                        lineInfo.LineNumber,
                        lineInfo.LinePosition,
                        ns,
                        dot < 0 ? reader.LocalName : reader.LocalName.Substring(0, dot)
                    )
                );
            }
        }
        catch (XmlException)
        {
            return Array.Empty<ElementTypeReference>();
        }

        return references;
    }

    /// <summary>Visits every attribute of every element. Returns false when the document is not
    /// well-formed, which a scanner treats as nothing to say rather than as an error.</summary>
    static bool ForEachAttribute(string content, Action<XmlReader, IXmlLineInfo, string?> visit)
    {
        try
        {
            using var reader = CreateReader(content);
            var lineInfo = (IXmlLineInfo)reader;
            var compatibility = new MarkupCompatibility();
            while (reader.Read())
            {
                if (
                    reader.NodeType != XmlNodeType.Element
                    || !compatibility.Reads(reader)
                    || !reader.HasAttributes
                )
                    continue;

                // Read before the loop moves the reader onto an attribute, where it names that.
                var tag = ResolveTag(reader);
                while (reader.MoveToNextAttribute())
                {
                    if (!compatibility.Drops(reader.NamespaceURI))
                        visit(reader, lineInfo, tag);
                }

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
    static string? ClrNamespaceOf(string value)
    {
        if (XamlMarkup.IsNoesisExtensions(value))
            return null;

        var body = value.Substring(ClrNamespace.Length);
        var semi = body.IndexOf(';');
        return semi < 0 ? body : body.Substring(0, semi);
    }

    // A prefix can be redeclared further in, so it resolves where the reader stands.
    internal static string? NamespaceOf(XmlReader reader, string prefix) =>
        reader.LookupNamespace(prefix) switch
        {
            null => prefix.Length == 0 ? PresentationNamespace : null,
            "" or PresentationUri => PresentationNamespace,
            var uri when uri.StartsWith(ClrNamespace, StringComparison.Ordinal) => ClrNamespaceOf(
                uri
            ),
            _ => null,
        };

    const string PresentationUri = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    static bool CollectDeclarations(string content, Dictionary<string, string> namedElements) =>
        ForEachAttribute(
            content,
            (reader, _, tag) =>
            {
                if (reader.Name != "x:Name" && reader.Name != "Name")
                    return;

                // Each template is its own name scope, so a name can repeat within a file.
                var type = tag ?? "";
                namedElements[reader.Value] =
                    namedElements.TryGetValue(reader.Value, out var seen) && seen != type
                        ? ""
                        : type;
            }
        );

    static bool Walk(
        string content,
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
            var compatibility = new MarkupCompatibility();

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || !compatibility.Reads(reader))
                    continue;

                // By depth: a dropped element's end tag would pop a frame it never pushed.
                stack.RemoveRange(reader.Depth + 1, stack.Count - reader.Depth - 1);

                var isEmpty = reader.IsEmptyElement;
                var parent = stack[stack.Count - 1];
                var frame = BuildFrame(reader, parent, stack, namedElements, walked, ordinal++);

                if (collect)
                    Collect(reader, frame.Ordinal, stack, walked);

                if (walked.ElementSources.TryGetValue(frame.Ordinal, out var written))
                {
                    frame.Sources ??= new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (var pair in written)
                        frame.Sources[pair.Key] = pair.Value;
                }

                if (reader.GetAttribute("CompileBindings", ToolkitNamespace) is { } opt)
                {
                    frame.Compile = XamlScopeRules.CompileBindings(opt) ?? false;
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
                    compatibility,
                    ref frame,
                    stack,
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
            Compile = parent.Compile,
            TargetType = parent.TargetType,
            SelfType = triggerLike ? parent.TargetType : ResolveTag(reader),
        };

        if (triggerLike)
        {
            var target = reader.GetAttribute("TargetName");
            var redirect = target ?? reader.GetAttribute("SourceName");
            frame.PropertyOwner =
                redirect is null ? parent.TargetType
                : namedElements.TryGetValue(redirect, out var tag) && tag.Length > 0 ? tag
                : null;

            if (target is not null)
                frame.SelfType = frame.PropertyOwner;
        }

        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            var propertyName = name.Substring(dot + 1);
            frame.PropertyName = propertyName;
            frame.Transparent = XamlScopeRules.TransparentHosts.Contains(propertyName);

            if (XamlScopeRules.TemplateHosts.TryGetValue(propertyName, out var host))
                frame.PendingHost = HostedScope(stack, host, walked.Derives);

            return frame;
        }

        frame.Breaks = XamlScopeRules.ScopeBreakers.Contains(name);

        var keyed = reader.GetAttribute("x:Key") is not null;

        switch (name)
        {
            // Content of any template but a ControlTemplate is templated by whatever presents it.
            case "DataTemplate":
            case "HierarchicalDataTemplate":
                frame.Data = DataTemplateScope(reader, parent, keyed, ordinal, walked);
                frame.TargetType = null;
                break;
            case "Style":
            case "ControlTemplate":
                frame.Data = keyed ? null : TemplatedScope(parent);
                frame.TargetType = ResolveTypeAttribute(reader, "TargetType") ?? parent.TargetType;
                frame.IsStyle = name == "Style";
                break;
            case "ItemsPanelTemplate":
                frame.Data = keyed ? null : TemplatedScope(parent);
                frame.TargetType = null;
                break;
            default:
                if (keyed)
                    frame.Data = frame.ParentData = null;
                break;
        }

        // Stated on the element, it names what its own DataContext resolves to, redirect and all.
        if (reader.GetAttribute("DataType", ToolkitNamespace) is { } annotated)
            frame.Data = ResolveTypeReference(reader, annotated) is { } resolved
                ? new DataScope(resolved)
                : null;
        else if (walked.Rebound.Contains(ordinal))
            frame.Data = null;
        else if (reader.GetAttribute("DataContext") is { } explicitContext)
            frame.Data = Redirected(frame.Data, explicitContext);

        return frame;
    }

    static DataScope? Redirected(DataScope? scope, string written)
    {
        if (
            scope is null
            || !XamlBindingMarkup.TryGetExtension(written, "Binding", out var body)
            || XamlBindingMarkup.ParseBinding(body) is not { RedirectsTyped: true } binding
        )
            return null;

        if (binding.IsIdentity)
            return scope;

        return XamlBindingMarkup.IsValidPath(binding.Path)
            ? scope.Extend(binding.Path!, false)
            : null;
    }

    static DataScope? DataTemplateScope(
        XmlReader reader,
        Frame parent,
        bool keyed,
        int ordinal,
        Walked walked
    )
    {
        if (ResolveTypeAttribute(reader, "DataType") is { } declared)
            return new DataScope(declared);

        if (keyed || parent.PendingHost is not { } hosted)
            return null;

        return reader.LocalName == "HierarchicalDataTemplate"
            ? EveryLevel(reader, ordinal, hosted, walked)
            : hosted;
    }

    static DataScope? EveryLevel(XmlReader reader, int ordinal, DataScope hosted, Walked walked)
    {
        if (
            reader.GetAttribute("ItemTemplate") is not null
            || Writes(walked, ordinal, "ItemTemplate")
        )
            return hosted;

        if (reader.GetAttribute("ItemsSource") is { } written)
            return HostPath(written) is { } path ? hosted.Recurring(path) : null;

        if (
            !walked.ElementSources.TryGetValue(ordinal, out var sources)
            || !sources.TryGetValue("ItemsSource", out var element)
        )
            return hosted;

        return element is null ? null : hosted.Recurring(element);
    }

    static bool Writes(Walked walked, int ordinal, string property) =>
        walked.PropertyElements.TryGetValue(ordinal, out var written) && written.Contains(property);

    static DataScope? TemplatedScope(Frame parent) =>
        parent.Transparent ? parent.Data : parent.PendingHost;

    // A breaker's ItemsSource feeds its own tree, so the walk stops at it and at the first list.
    static DataScope? HostedScope(
        List<Frame> stack,
        XamlScopeRules.TemplateHost host,
        Func<string, string, bool>? derives
    )
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            var frame = stack[i];
            if (frame.Breaks && i < stack.Count - 1)
                return null;

            if (
                frame.Sources is not null
                && frame.Sources.TryGetValue(host.SourceProperty, out var path)
            )
            {
                if (path is null)
                    return null;

                // A pathless source hands on the DataContext itself, unchanged.
                return path.Length == 0 && !host.IsCollection
                    ? frame.Data
                    : frame.Data?.Extend(path, host.IsCollection);
            }

            if (frame.Breaks || !host.SourcedAbove || Lists(frame, derives))
                return null;
        }

        return null;
    }

    static bool Lists(Frame frame, Func<string, string, bool>? derives) =>
        frame.SelfType is { } self
        && derives?.Invoke(self, PresentationNamespace + ".ItemsControl") == true;

    static void ProcessAttributes(
        XmlReader reader,
        IXmlLineInfo lineInfo,
        MarkupCompatibility compatibility,
        ref Frame frame,
        List<Frame> stack,
        Dictionary<string, string> namedElements,
        Dictionary<string, DataScope?> nameToScope,
        bool collect,
        Walked walked
    )
    {
        if (!reader.HasAttributes)
            return;

        var triggerLike = TriggerLike.Contains(reader.LocalName);
        Dictionary<string, string?>? sources = null;

        while (reader.MoveToNextAttribute())
        {
            if (reader.Name.StartsWith("xmlns", StringComparison.Ordinal))
                continue;

            // A design-time value binds against the designer's DataContext, not the real one.
            if (compatibility.Drops(reader.NamespaceURI))
                continue;

            var value = reader.Value;
            var line = lineInfo.LineNumber;
            var column = lineInfo.LinePosition;

            if (IsSourceProperty(reader.LocalName))
            {
                sources ??= new Dictionary<string, string?>(StringComparer.Ordinal);
                sources[reader.LocalName] = HostPath(value);
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
                reader,
                XamlBindingMarkup.ParseBinding(body),
                line,
                column,
                frame,
                stack,
                reader.LocalName == "DataContext",
                namedElements,
                nameToScope,
                walked
            );
        }

        reader.MoveToElement();

        if (sources is not null)
        {
            // Merged, not assigned: a property-element source was already recorded on this frame.
            frame.Sources ??= new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var pair in sources)
                frame.Sources[pair.Key] = pair.Value;
        }
    }

    static void EmitCheck(
        XmlReader reader,
        BindingExpression binding,
        int line,
        int column,
        Frame frame,
        List<Frame> stack,
        bool isDataContext,
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
                    AncestorTypeName(reader, relative) is { } target
                    && AncestorScope(stack, target, AncestorLevel(relative), walked.Derives)
                        is { } ancestorScope
                )
                    walked.Checks.Add(new BindingCheck(line, column, ancestorScope, ancestorTail));
            }
            else if (AncestorTypeName(reader, relative) is { } ancestor)
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
            else if (namedElements.TryGetValue(binding.ElementName, out var tag) && tag.Length > 0)
            {
                walked.Checks.Add(new BindingCheck(line, column, new DataScope(tag), path));
            }
            return;
        }

        var scope = isDataContext ? frame.ParentData : frame.Data;
        if (scope is not null)
        {
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
        int level,
        Func<string, string, bool>? derives
    )
    {
        for (var i = stack.Count - 1; i >= 0 && level > 0; i--)
        {
            if (stack[i].Breaks)
                return null;

            if (stack[i].SelfType is not { } self)
                continue;

            if ((self == targetType || derives?.Invoke(self, targetType) == true) && --level == 0)
                return stack[i].Data;
        }
        return null;
    }

    // Zero for anything the engine would refuse, which leaves the binding unchecked.
    static int AncestorLevel(string relativeSource)
    {
        if (XamlMarkupParser.Parse(relativeSource) is not { } call)
            return 0;

        foreach (var argument in call.Named)
        {
            if (argument.Key == "AncestorLevel")
                return
                    argument.Value is string text
                    && int.TryParse(
                        text.Trim(),
                        System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var level
                    )
                    ? level
                    : 0;
        }

        return 1;
    }

    static bool IsSourceProperty(string name) => name is "Content" or "ItemsSource" or "Header";

    // Empty hands on the DataContext itself; null is a source the walk cannot type.
    static string? HostPath(string written)
    {
        if (
            XamlBindingMarkup.ParseValue(written)
            is not {
                RelativeSource: null,
                ElementName: null,
                HasSource: false,
                Converts: false,
                PositionalCalls: false,
            } binding
        )
            return null;

        if (binding.IsIdentity)
            return "";

        return XamlBindingMarkup.IsValidPath(binding.Path) ? binding.Path : null;
    }

    static void Collect(XmlReader reader, int ordinal, List<Frame> stack, Walked walked)
    {
        var parent = stack[stack.Count - 1];
        var name = reader.LocalName;

        var dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            var property = name.Substring(dot + 1);
            if (!walked.PropertyElements.TryGetValue(parent.Ordinal, out var written))
                walked.PropertyElements[parent.Ordinal] = written = new HashSet<string>(
                    StringComparer.Ordinal
                );
            written.Add(property);

            if (IsSourceProperty(property))
                RecordSource(walked, parent.Ordinal, property, null);
            return;
        }

        if (stack.Count >= 2 && parent.PropertyName is { } held && IsSourceProperty(held))
        {
            RecordSource(
                walked,
                stack[stack.Count - 2].Ordinal,
                held,
                ordinal == parent.Ordinal + 1 && name == "Binding"
                    ? ElementBindingPath(reader)
                    : null
            );
        }
        else if (
            stack.Count >= 3
            && parent.SelfType == PresentationNamespace + ".Binding"
            && stack[stack.Count - 2] is { PropertyName: { } outer } slot
            && IsSourceProperty(outer)
            && parent.Ordinal == slot.Ordinal + 1
        )
        {
            RecordSource(walked, stack[stack.Count - 3].Ordinal, outer, null);
        }

        CollectStyle(reader, ordinal, stack, walked.Styles);
    }

    static void RecordSource(Walked walked, int owner, string property, string? path)
    {
        if (!walked.ElementSources.TryGetValue(owner, out var sources))
            walked.ElementSources[owner] = sources = new Dictionary<string, string?>(
                StringComparer.Ordinal
            );

        sources[property] = path;
    }

    static string? ElementBindingPath(XmlReader reader)
    {
        if (
            reader.GetAttribute("Source") is not null
            || reader.GetAttribute("RelativeSource") is not null
            || reader.GetAttribute("ElementName") is not null
            || reader.GetAttribute("Converter") is not null
            || reader.GetAttribute("StringFormat") is not null
        )
            return null;

        var path = (reader.GetAttribute("Path") ?? "").Trim();
        if (path is "" or ".")
            return "";

        return XamlBindingMarkup.IsValidPath(path) ? path : null;
    }

    static void CollectStyle(XmlReader reader, int ordinal, List<Frame> stack, StyleFacts styles)
    {
        var parent = stack[stack.Count - 1];
        var name = reader.LocalName;
        var styled =
            parent.PropertyName == "Style" && stack.Count >= 2
                ? stack[stack.Count - 2].Ordinal
                : (int?)null;

        if (name == "Style")
        {
            if (reader.GetAttribute("x:Key") is { } key)
                Add(styles.Keyed, key, ordinal);
            if (ResourceKey(reader.GetAttribute("BasedOn")) is { } basedOn)
                styles.BasedOn[ordinal] = basedOn;
            if (styled is { } inline)
                Add(styles.Inline, inline, ordinal);
        }
        else if (
            name is "StaticResource" or "DynamicResource"
            && styled is { } holder
            && reader.GetAttribute("ResourceKey")?.Trim() is { Length: > 0 } held
        )
        {
            Add(styles.Referenced, holder, held);
        }
        else if (
            name == "Setter"
            && reader.GetAttribute("TargetName") is null
            && reader.GetAttribute("Property") is { } property
            && property.Trim().Trim('(', ')') is var written
            && written.Substring(written.LastIndexOf('.') + 1) == "DataContext"
        )
        {
            foreach (var frame in stack)
            {
                if (frame.IsStyle)
                    styles.SetDataContext.Add(frame.Ordinal);
            }
        }

        if (ResourceKey(reader.GetAttribute("Style")) is { } referenced)
            Add(styles.Referenced, ordinal, referenced);
    }

    static string? ResourceKey(string? written) =>
        written is not null
        && XamlMarkupParser.Parse(written.Trim())
            is { Name: "StaticResource" or "DynamicResource" } call
            ? (
                call.Positional.FirstOrDefault()
                ?? call.Named.FirstOrDefault(p => p.Key == "ResourceKey").Value as string
            )?.Trim()
            : null;

    static void Add<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> map, TKey key, TValue value)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var values))
            map[key] = values = new HashSet<TValue>();
        values.Add(value);
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
    static string? AncestorTypeName(XmlReader reader, string relativeSource)
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

            return reference is null ? null : ResolveTypeReference(reader, reference);
        }

        return null;
    }

    static string? ResolveTypeAttribute(XmlReader reader, string attribute) =>
        reader.GetAttribute(attribute) is { } raw ? ResolveTypeReference(reader, raw) : null;

    static string? ResolveTypeReference(XmlReader reader, string raw)
    {
        var value = raw.Trim();
        if (XamlBindingMarkup.TryGetExtension(value, "x:Type", out var inner))
            value = inner.Trim();

        return value.IndexOf('{') >= 0 ? null : ResolveQualified(reader, value);
    }

    static string? ResolveQualified(XmlReader reader, string qualified)
    {
        var colon = qualified.IndexOf(':');
        var name = colon < 0 ? qualified : qualified.Substring(colon + 1);
        if (
            !XamlMarkup.IsIdentifier(name)
            || NamespaceOf(reader, colon < 0 ? "" : qualified.Substring(0, colon)) is not { } ns
        )
            return null;

        return ns.Length == 0 ? name : ns + "." + name;
    }

    static string? ResolveTag(XmlReader reader) =>
        reader.LocalName.IndexOf('.') >= 0
            ? null
            : ResolveQualified(
                reader,
                reader.Prefix.Length == 0
                    ? reader.LocalName
                    : reader.Prefix + ":" + reader.LocalName
            );

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
        public Dictionary<string, string?>? Sources;
        public int Ordinal;
        public string? PropertyName;
        public bool Transparent;
        public bool Breaks;
        public bool Compile;
        public bool IsStyle;
    }
}

/// <summary>What <c>mc:Ignorable</c> drops, tracked as a reader moves forward through a document:
/// an element in an ignorable namespace goes with everything inside it, and an attribute in one
/// goes alone.</summary>
internal sealed class MarkupCompatibility
{
    const string Namespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    readonly List<HashSet<string>?> _ignorable = new List<HashSet<string>?> { null };

    int _dropped = -1;

    /// <summary>Whether the parser reads the element the reader stands on. Every element has to
    /// pass through here, in document order.</summary>
    public bool Reads(XmlReader reader)
    {
        var depth = reader.Depth;
        if (_dropped >= 0 && depth > _dropped)
            return false;

        _dropped = -1;
        _ignorable.RemoveRange(depth + 1, _ignorable.Count - depth - 1);
        if (Ignores(_ignorable[depth], reader.NamespaceURI))
        {
            _dropped = depth;
            return false;
        }

        _ignorable.Add(Declared(reader, _ignorable[depth]));
        return true;
    }

    public bool Drops(string namespaceUri) =>
        Ignores(_ignorable[_ignorable.Count - 1], namespaceUri);

    static bool Ignores(HashSet<string>? ignorable, string namespaceUri) =>
        namespaceUri == Namespace || ignorable?.Contains(namespaceUri) == true;

    static HashSet<string>? Declared(XmlReader reader, HashSet<string>? inherited)
    {
        if (reader.GetAttribute("Ignorable", Namespace) is not { } listed)
            return inherited;

        var ignorable = inherited is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(inherited, StringComparer.Ordinal);
        foreach (var prefix in listed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (reader.LookupNamespace(prefix) is { } uri)
                ignorable.Add(uri);
        }

        return ignorable;
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

    public DataScope Extend(string path, bool isCollection) =>
        Append(new Hop(path, isCollection, false));

    /// <summary>This same DataContext, known only where the items <paramref name="path"/> reads
    /// off it are of its own type: a hierarchical template is reused for every level below.</summary>
    public DataScope Recurring(string path) => Append(new Hop(path, true, true));

    DataScope Append(Hop hop)
    {
        var hops = new Hop[Hops.Length + 1];
        Array.Copy(Hops, hops, Hops.Length);
        hops[Hops.Length] = hop;
        return new DataScope(RootType, hops);
    }

    public readonly struct Hop
    {
        public Hop(string path, bool isCollection, bool recurs)
        {
            Path = path;
            IsCollection = isCollection;
            Recurs = recurs;
        }

        /// <summary>Empty for the DataContext itself.</summary>
        public string Path { get; }

        /// <summary>The hop lands on a collection; the DataContext is its element type.</summary>
        public bool IsCollection { get; }

        /// <summary>The hop only checks that the collection holds the type already reached, and
        /// leaves the DataContext where it was.</summary>
        public bool Recurs { get; }
    }
}

/// <summary>One <c>{x:Static prefix:Type.Member}</c> reference and where it sits in the file.</summary>
internal readonly struct StaticReference
{
    public StaticReference(
        int line,
        int column,
        string @namespace,
        string typeName,
        string memberName
    )
    {
        Line = line;
        Column = column;
        Namespace = @namespace;
        TypeName = typeName;
        MemberName = memberName;
    }

    public int Line { get; }
    public int Column { get; }

    public string Namespace { get; }

    public string TypeName { get; }
    public string MemberName { get; }
}

/// <summary>One <c>xmlns:prefix="clr-namespace:N"</c> declaration and where it sits in the file.</summary>
internal readonly struct ElementTypeReference
{
    public ElementTypeReference(int line, int column, string @namespace, string typeName)
    {
        Line = line;
        Column = column;
        Namespace = @namespace;
        TypeName = typeName;
    }

    public int Line { get; }
    public int Column { get; }
    public string Namespace { get; }
    public string TypeName { get; }
}

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
