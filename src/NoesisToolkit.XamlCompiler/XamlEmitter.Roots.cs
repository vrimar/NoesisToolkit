using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

readonly record struct MarkupSite(string Message, int Line, int Column);

sealed partial class XamlEmitter
{
    public readonly TrimRoots Roots = new TrimRoots();

    public readonly List<MarkupSite> Unrooted = new List<MarkupSite>();

    public readonly List<MarkupSite> LeftNative = new List<MarkupSite>();

    public readonly List<XAttribute> UnresolvedTypes = new List<XAttribute>();

    XAttribute? _attribute;

    string? _parsedBy;

    readonly JournaledSet<(XElement, string)> _walked = new JournaledSet<(XElement, string)>();

    HashSet<ITypeSymbol>? _referenced;

    const string ItemTypeAttribute = "ItemType";

    readonly struct RootsMark(int roots, int unrooted)
    {
        public int Roots { get; } = roots;
        public int Unrooted { get; } = unrooted;
    }

    RootsMark MarkRoots() => new RootsMark(Roots.Count, Unrooted.Count);

    void RestoreRoots(RootsMark mark)
    {
        Roots.Truncate(mark.Roots);
        Unrooted.RemoveRange(mark.Unrooted, Unrooted.Count - mark.Unrooted);
    }

    const string ContextUndeclared =
        "reads a DataContext whose type is not declared; state it with ntk:DataType on an enclosing element";

    const string ElementContextUndeclared =
        "reads the DataContext of an element whose type is not declared; state it with ntk:DataType on that element";

    const string AncestorContextUndeclared =
        "reads an ancestor's DataContext whose type is not declared; state it with ntk:AncestorDataType";

    const string RowUndeclared =
        "reads a row whose type is not declared; bind the list's ItemsSource to a typed path, or state the row type with ntk:ItemType on the column";

    const string ItemsUndeclared =
        "reads items whose type is not declared; bind ItemsSource to a typed path, or state the item type with ntk:ItemType";

    void RootBinding(XElement element, MarkupCall call, string? slot)
    {
        if (BindingPath(call) is not { } path)
            return;

        var (problem, _) = Quietly(() => BindingRoots(element, call, slot, path));
        if (problem is not null)
            ReportUnrooted(element, $"binding '{path}' {problem}");
    }

    static string? BindingPath(MarkupCall call)
    {
        if (call.Name != "Binding" || call.PositionalCalls.Count > 0)
            return null;

        var path = (
            call.Positional.FirstOrDefault() ?? NamedValue(call, "Path") as string ?? ""
        ).Trim();
        return path.Length == 0 || path == "." ? null : path;
    }

    ITypeSymbol? BindingValueType(XElement element, MarkupCall call, string? slot)
    {
        if (call.Name != "Binding" || call.PositionalCalls.Count > 0)
            return null;

        var mark = MarkRoots();
        var (_, type) = Quietly(() => BindingRoots(element, call, slot, BindingPath(call) ?? ""));
        RestoreRoots(mark);
        return type;
    }

    T Quietly<T>(Func<T> resolve)
    {
        var quiet = _quiet;
        _quiet = true;
        try
        {
            return resolve();
        }
        finally
        {
            _quiet = quiet;
        }
    }

    (string? Problem, ITypeSymbol? Type) BindingRoots(
        XElement element,
        MarkupCall call,
        string? slot,
        string path
    )
    {
        if (NamedValue(call, "Source") is { } stated)
            return ExplicitSourceType(element, stated) is { } explicitType
                ? SourceRoots(element, explicitType, path)
                : ("reads a Source the compiler cannot type", null);

        if (slot == "DisplayMemberBinding")
            return RowType(element) is { } row
                ? SourceRoots(element, row, path)
                : (RowUndeclared, null);

        var named = (NamedValue(call, "ElementName") as string)?.Trim();
        var relative = NamedValue(call, "RelativeSource") as MarkupCall;
        var mode = relative is null ? null : RelativeSourceParts(relative).Mode;

        if (named is null && (relative is null || mode == "PreviousData"))
        {
            // DataContext is read off the parent: the element's own is what this binding sets.
            var scope = slot == "DataContext" ? BindingHost(element).Parent : element;
            return scope is not null && DeclaredContext(scope) is { } context
                ? SourceRoots(element, context, path)
                : (ContextUndeclared, null);
        }

        var found =
            mode == "Self"
                ? SelfSource(element)
                : ResolveSource(element, call)
                    ?? DistantAncestor(element, relative)
                    ?? NamedAnywhere(element, named);

        if (found is not { } source)
            return (
                named is not null
                    ? $"names an element '{named}' the document does not declare once"
                    : "reads a RelativeSource the compiler cannot place",
                null
            );

        if (TrySplitDataContextHop(path, out var tail))
        {
            var context =
                source.Detached ? AncestorContext(source.Scope)
                : source.Templated ? TemplatedContext(source.Scope)
                : DeclaredContext(source.Scope);

            return context is null
                ? (source.Detached ? AncestorContextUndeclared : ElementContextUndeclared, null)
                : SourceRoots(element, context, tail);
        }

        return (source.Type ?? TypeOfElement(source.Scope)) is { } elementType
            ? SourceRoots(element, elementType, path)
            : ("reads an element whose type does not resolve", null);
    }

    // An attached owner's prefix resolves where the binding is written, not at its source.
    (string? Problem, ITypeSymbol? Type) SourceRoots(
        XElement element,
        ITypeSymbol source,
        string path
    ) => path.Length == 0 ? (null, source) : PathRoots(element, source, path);

    BindingSource? SelfSource(XElement element)
    {
        var host = BindingHost(element);
        var type =
            resolver.SymbolOf(host) is { } hostType && IsSetterLike(hostType)
                ? SetterTarget(host)
                : TypeOfElement(host);

        return type is null ? null : new BindingSource("", host, type);
    }

    // An x:Class root is its class, not the tag it is written as.
    INamedTypeSymbol? TypeOfElement(XElement element) =>
        element.Parent is null && _rootClass is not null ? _rootClass : resolver.SymbolOf(element);

    // The engine searches every name scope outward, so a name declared once is the one it finds.
    BindingSource? NamedAnywhere(XElement element, string? name)
    {
        if (name is null)
            return null;

        var declared = LookupNamed(name);
        if (declared is null)
        {
            var matches = element
                .Document?.Root?.DescendantsAndSelf()
                .Where(e =>
                    e.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs))?.Value == name
                )
                .Take(2)
                .ToList();
            declared = matches is { Count: 1 } ? matches[0] : null;
        }

        return declared is not null && TypeOfElement(declared) is { } type
            ? new BindingSource("", declared, type)
            : null;
    }

    // Past the nearest ancestor only its own type is known: the engine counts AncestorLevel.
    BindingSource? DistantAncestor(XElement element, MarkupCall? relative)
    {
        if (relative is not { Name: "RelativeSource" })
            return null;

        var (_, reference, _) = RelativeSourceParts(relative);
        return reference is not null && ResolveTypeSymbol(element, reference) is { } type
            ? new BindingSource("", element, type, detached: true)
            : null;
    }

    INamedTypeSymbol? ExplicitSourceType(XElement element, object source)
    {
        if (source is not MarkupCall call)
            return null;

        if (call.Name is "x:Static" or "Static")
        {
            var reference = call.Positional.FirstOrDefault() ?? "";
            var dot = reference.LastIndexOf('.');
            if (
                dot <= 0
                || ResolveTypeSymbol(element, reference.Substring(0, dot)) is not { } owner
            )
                return null;

            return owner.GetMembers(reference.Substring(dot + 1)).FirstOrDefault() switch
            {
                IPropertySymbol { IsStatic: true } property => property.Type as INamedTypeSymbol,
                IFieldSymbol { IsStatic: true } field => field.Type as INamedTypeSymbol,
                _ => null,
            };
        }

        return
            call.Name == "StaticResource"
            && call.Positional.FirstOrDefault() is { } key
            && DeclarationOf(element, key.Trim()) is { } declaration
            ? resolver.SymbolOf(declaration)
            : null;
    }

    INamedTypeSymbol? RowType(XElement element)
    {
        var column = BindingHost(element);
        if (StatedItemType(column) is { } stated)
            return ResolveTypeSymbol(column, stated.Value);

        return SourcedContext(column, XamlScopeRules.TemplateHosts["CellTemplate"]);
    }

    static XAttribute? StatedItemType(XElement element) =>
        element.Attribute(XName.Get(ItemTypeAttribute, XamlTypeResolver.ToolkitNs));

    static XElement BindingHost(XElement element)
    {
        var current = element;
        while (
            current.Parent is { } parent
            && (
                current.Name.LocalName is "Binding" or "MultiBinding"
                || current.Name.LocalName.IndexOf('.') >= 0
            )
        )
            current = parent;

        return current;
    }

    static string? SlotAround(XElement binding)
    {
        for (var current = binding.Parent; current is not null; current = current.Parent)
        {
            var local = current.Name.LocalName;
            if (local == "MultiBinding")
                continue;

            var dot = local.IndexOf('.');
            return dot < 0 ? null : local.Substring(dot + 1);
        }

        return null;
    }

    readonly struct PathSegment(string name, string? owner, bool indexed)
    {
        public string Name { get; } = name;

        public string? Owner { get; } = owner;

        public bool Indexed { get; } = indexed;
    }

    static List<PathSegment>? Segments(string path)
    {
        var segments = new List<PathSegment>();
        var i = 0;
        while (i < path.Length)
        {
            string name;
            string? owner = null;
            if (path[i] == '(')
            {
                var close = path.IndexOf(')', i);
                if (close < 0)
                    return null;

                var inside = path.Substring(i + 1, close - i - 1).Trim();
                var dot = inside.LastIndexOf('.');
                if (dot <= 0)
                    return null;

                owner = inside.Substring(0, dot);
                name = inside.Substring(dot + 1);
                i = close + 1;
            }
            else
            {
                var end = i;
                while (end < path.Length && path[end] is not ('.' or '['))
                    end++;

                name = path.Substring(i, end - i).Trim();
                i = end;
            }

            if (!XamlMarkup.IsIdentifier(name))
                return null;

            var indexed = false;
            if (i < path.Length && path[i] == '[')
            {
                var close = path.IndexOf(']', i);
                if (close < 0)
                    return null;

                indexed = true;
                i = close + 1;
            }

            segments.Add(new PathSegment(name, owner, indexed));

            if (i < path.Length && path[i] != '.')
                return null;

            i++;
        }

        return segments.Count == 0 ? null : segments;
    }

    (string? Problem, ITypeSymbol? Type) PathRoots(XElement scope, ITypeSymbol source, string path)
    {
        if (Segments(path) is not { } segments)
            return ("has a path the compiler cannot read", null);

        var current = source;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            if (segment.Owner is not null)
            {
                if (ResolveTypeSymbol(scope, segment.Owner) is not { } owner)
                    return (
                        $"names an attached owner '{segment.Owner}' that does not resolve",
                        null
                    );

                RootDependencyProperty(owner, segment.Name);
                if (resolver.FindAttachedValueType(owner, segment.Name) is not { } value)
                    return (
                        $"reads '{segment.Owner}.{segment.Name}', which is not an attached property",
                        null
                    );

                current = value;
            }
            else
            {
                // The engine reads the boxed value, which is never a Nullable<T>.
                if (TrimRoots.Unwrapped(current) is not INamedTypeSymbol owner || Untyped(owner))
                    return (
                        i == 0
                            ? $"reads '{segment.Name}' off a source typed {Display(current)}"
                            : $"steps through '{segments[i - 1].Name}', typed {Display(current)}, which says nothing about what it holds",
                        null
                    );

                _referenced?.Add(owner);
                var (read, unimplemented) = RootHop(owner, segment.Name);
                if (unimplemented is not null)
                {
                    return (
                        $"reads '{segment.Name}' through {Display(unimplemented)}, which no class the compiler can see implements",
                        null
                    );
                }
                else if (read is not null)
                {
                    current = read;
                }
                else if (owner.ContainingAssembly?.Name == "Noesis.GUI")
                {
                    return (null, null);
                }
                else
                {
                    return ($"reads '{segment.Name}', which {Display(owner)} does not have", null);
                }
            }

            if (segment.Indexed)
            {
                if (Indexed(current) is not { } item)
                    return (
                        $"indexes '{segment.Name}', typed {Display(current)}, which has no element type",
                        null
                    );

                current = item;
            }
        }

        Roots.EnumLiterals(current);
        if (_referenced is not null)
        {
            _referenced.Add(current);
            if (ElementType(current) is { } item)
                _referenced.Add(item);
        }

        return (null, current);
    }

    static bool Untyped(INamedTypeSymbol type) => type.SpecialType == SpecialType.System_Object;

    // The engine reads the run-time object, so a member only a derived class declares still resolves.
    (ITypeSymbol? Read, ITypeSymbol? Unimplemented) RootHop(INamedTypeSymbol owner, string name)
    {
        if (RootMember(owner, name) is { Read: not null } declared)
            return declared;

        ITypeSymbol? found = null;
        if (!owner.IsSealed)
        {
            foreach (var derived in Derived(owner))
            {
                var read = RootMember(derived, name).Read;
                found ??= read;
            }
        }

        return (found, null);
    }

    (ITypeSymbol? Read, ITypeSymbol? Unimplemented) RootMember(INamedTypeSymbol owner, string name)
    {
        if (resolver.FindProperty(owner, name) is { } property)
        {
            if (IsDependencyProperty(owner, name))
            {
                RootDependencyProperty(owner, name);
                return (property.Type, null);
            }

            return RootProperty(property, owner) ? (property.Type, null) : (property.Type, owner);
        }

        if (resolver.FindGeneratedCommand(owner, name) is { } command)
        {
            if (CommandOwner(owner, name) is { } declaring)
                Roots.Member(declaring, name);

            return (command, null);
        }

        return (null, null);
    }

    // A DP's CLR wrapper kept without its field would register a plain property that shadows it.
    bool IsDependencyProperty(INamedTypeSymbol owner, string name) =>
        XamlTypeResolver.DerivesFrom(owner, "global::Noesis.DependencyObject")
        && resolver.FindDependencyPropertyOwner(owner, name) is not null;

    IEnumerable<INamedTypeSymbol> Derived(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        foreach (var candidate in resolver.ApplicationTypes())
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, definition))
                continue;

            var related =
                type.TypeKind == TypeKind.Interface
                    ? candidate.AllInterfaces.Any(i =>
                        SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, definition)
                    )
                    : Inherits(candidate, definition);

            if (related)
                yield return candidate;
        }
    }

    static bool Inherits(INamedTypeSymbol candidate, INamedTypeSymbol definition)
    {
        for (var current = candidate.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, definition))
                return true;
        }

        return false;
    }

    static string Display(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();

    // An IList is read by the engine's own list callbacks; any other indexer through reflection.
    ITypeSymbol? Indexed(ITypeSymbol collection)
    {
        if (collection is IArrayTypeSymbol array)
            return array.ElementType;

        IPropertySymbol? indexer = null;
        for (
            var current = collection;
            current is not null && indexer is null;
            current = current.BaseType
        )
        {
            indexer = current
                .GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p =>
                    p.IsIndexer
                    && p.MetadataName == "Item"
                    && p.DeclaredAccessibility == Accessibility.Public
                    && p.Parameters.Length == 1
                    && p.Parameters[0].Type.SpecialType
                        is SpecialType.System_Int32
                            or SpecialType.System_String
                );
        }

        var list = collection.AllInterfaces.Any(i =>
            XamlTypeResolver.Fqn(i) == "global::System.Collections.IList"
        );

        if (indexer is not null && !list && indexer.ContainingType is { } declaring)
            Roots.Member(declaring, indexer.MetadataName);

        return indexer?.Type ?? ElementType(collection);
    }

    static INamedTypeSymbol? CommandOwner(INamedTypeSymbol type, string name)
    {
        var method = name.Substring(0, name.Length - "Command".Length);
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (
                current
                    .GetMembers(method)
                    .OfType<IMethodSymbol>()
                    .Any(m =>
                        m.GetAttributes()
                            .Any(a =>
                                a.AttributeClass?.ToDisplayString()
                                == "NoesisToolkit.Mvvm.DelegateCommandAttribute"
                            )
                    )
            )
                return current;
        }

        return null;
    }

    // The engine reads the property the object's class declares, never the interface's.
    bool RootProperty(IPropertySymbol property, INamedTypeSymbol? through = null)
    {
        if (property.ContainingType is not { } declaring)
            return true;

        if (declaring.TypeKind != TypeKind.Interface)
        {
            Roots.Member(declaring, property.Name);
            return true;
        }

        var implemented = false;
        foreach (var (implementation, member) in Implementations(property, through ?? declaring))
        {
            implemented = true;
            if (!IsDependencyProperty(implementation, member.Name))
                Roots.Member(member.ContainingType, member.Name);
        }

        // A framework class implementing it is never a candidate, so none found proves nothing.
        return implemented || XamlTypeResolver.Framework(declaring.ContainingAssembly.Name);
    }

    // A generic class implements the interface over its own type parameters, so map onto that one.
    IEnumerable<(INamedTypeSymbol Type, ISymbol Member)> Implementations(
        ISymbol member,
        INamedTypeSymbol through
    )
    {
        var definition = member.ContainingType.OriginalDefinition;
        foreach (var candidate in Derived(through))
        {
            foreach (var face in candidate.AllInterfaces)
            {
                if (!SymbolEqualityComparer.Default.Equals(face.OriginalDefinition, definition))
                    continue;

                foreach (var mapped in face.GetMembers(member.Name))
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            mapped.OriginalDefinition,
                            member.OriginalDefinition
                        )
                        && candidate.FindImplementationForInterfaceMember(mapped)
                            is {
                                DeclaredAccessibility: Accessibility.Public,
                                IsStatic: false,
                            } implemented
                        && implemented.Name == member.Name
                    )
                        yield return (candidate, implemented);
                }
            }
        }
    }

    // Noesis registers a DP through its owner's class constructor, which trimming keeps only while the field is reachable.
    void RootDependencyProperty(INamedTypeSymbol owner, string name)
    {
        if (resolver.FindDependencyPropertyOwner(owner, name) is { } declaring)
            Roots.Member(declaring, name + "Property");
    }

    static readonly System.Text.RegularExpressions.Regex PrefixedAttachedSegment = new(
        @"\((\w+:\w+)\.(\w+)\)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    void RootPropertyPath(XElement scope, string path)
    {
        foreach (
            System.Text.RegularExpressions.Match match in PrefixedAttachedSegment.Matches(path)
        )
        {
            if (ResolveTypeSymbol(scope, match.Groups[1].Value) is { } owner)
                RootDependencyProperty(owner, match.Groups[2].Value);
        }
    }

    void RootNamedByText(XElement element, INamedTypeSymbol type, string name, string value)
    {
        switch (name)
        {
            case "DisplayMemberPath":
            case "SelectedValuePath":
                RootItemPath(element, name, value.Trim());
                return;
            case "EventName"
                when XamlTypeResolver.DerivesFrom(type, "global::Noesis.Interactivity.TriggerBase"):
                RootTriggerEvent(element, value.Trim());
                return;
        }
    }

    void RootItemPath(XElement element, string name, string path)
    {
        if (path.Length == 0)
            return;

        foreach (var item in ItemTypes(element))
        {
            if (item is null)
            {
                ReportUnrooted(element, $"{name} '{path}' {ItemsUndeclared}");
                return;
            }

            var (problem, _) = Quietly(() => PathRoots(element, item, path));
            if (problem is not null)
            {
                ReportUnrooted(element, $"{name} '{path}' {problem}");
                return;
            }
        }
    }

    // Items written inline are typed by their tags; a null stands for items the compiler cannot see.
    IEnumerable<INamedTypeSymbol?> ItemTypes(XElement element)
    {
        if (StatedItemType(element) is { } stated)
        {
            yield return ResolveTypeSymbol(element, stated.Value);
            yield break;
        }

        if (element.Attribute("ItemsSource") is { } source)
        {
            var items = Quietly(() => SourceType(element, source.Value));
            yield return items is null ? null : ElementType(items);
            yield break;
        }

        var inline = element
            .Elements()
            .SelectMany(e =>
                e.Name.LocalName.EndsWith(".Items", StringComparison.Ordinal) ? e.Elements()
                : e.Name.LocalName.IndexOf('.') < 0 ? new[] { e }
                : Enumerable.Empty<XElement>()
            )
            .Select(e => resolver.SymbolOf(e))
            .Distinct<INamedTypeSymbol?>(SymbolEqualityComparer.Default)
            .ToList();

        if (inline.Count == 0)
            yield return null;

        foreach (var type in inline)
            yield return type;
    }

    void RootTriggerEvent(XElement trigger, string name)
    {
        if (!XamlMarkup.IsIdentifier(name))
            return;

        ITypeSymbol? source = null;
        var stated = trigger.Attribute("Source") ?? trigger.Attribute("SourceObject");
        if (stated is not null)
        {
            if (
                XamlMarkupParser.IsMarkup(stated.Value)
                && XamlMarkupParser.Parse(stated.Value) is { Name: "Binding" } call
            )
                source = BindingValueType(trigger, call, stated.Name.LocalName);
        }
        else if (trigger.Attribute("SourceName")?.Value is { } sourceName)
        {
            source = LookupName(sourceName.Trim());
        }
        else if (trigger.Parent is { Name.LocalName: "Interaction.Triggers", Parent: { } host })
        {
            source = TypeOfElement(host);
        }

        if (source is not INamedTypeSymbol owner || Untyped(owner))
        {
            ReportUnrooted(
                trigger,
                $"EventName '{name}' names an event on a source whose type is not declared"
            );
            return;
        }

        if (resolver.FindEvent(owner, name) is not { ContainingType: { } declaring } found)
        {
            ReportUnrooted(
                trigger,
                $"EventName '{name}' is not a public event of {Display(owner)}"
            );
            return;
        }

        if (declaring.TypeKind != TypeKind.Interface)
        {
            Roots.Member(declaring, name);
            return;
        }

        var implemented = false;
        foreach (var (_, member) in Implementations(found, declaring))
        {
            implemented = true;
            Roots.Member(member.ContainingType, member.Name);
        }

        if (!implemented && !XamlTypeResolver.Framework(declaring.ContainingAssembly.Name))
            ReportUnrooted(
                trigger,
                $"EventName '{name}' names an event of {Display(declaring)}, which no class the compiler can see implements"
            );
    }

    void RootParsed(XElement element, string parsedBy, bool constructs = true)
    {
        var outer = _parsedBy;
        // A declaration copied into several probes is walked once per probe but reported once.
        _parsedBy = _walked.Add((element, parsedBy)) ? parsedBy : null;
        try
        {
            RootParsedTree(element, constructs);
        }
        finally
        {
            _parsedBy = outer;
        }
    }

    void RootParsedTree(XElement element, bool constructs = true)
    {
        var local = element.Name.LocalName;

        if (local == "Binding")
        {
            if (BindingElementCall(element) is { } call)
            {
                RootBinding(element, call, SlotAround(element));
                ReportParsedBinding(element);
            }

            foreach (var child in element.Elements())
                RootParsedTree(child);
            return;
        }

        var dot = local.IndexOf('.');
        if (dot >= 0)
        {
            if (
                ResolveIn(element, element.Name.NamespaceName, local.Substring(0, dot)) is { } owner
            )
            {
                var name = local.Substring(dot + 1);
                if (
                    element.Parent is { } parent
                    && TypeOfElement(parent) is { } parentType
                    && XamlTypeResolver.DerivesFrom(parentType, XamlTypeResolver.Fqn(owner))
                )
                    RootSetProperty(owner, name);
                else
                    RootDependencyProperty(owner, name);
            }

            foreach (var child in element.Elements())
                RootParsedTree(child);
            return;
        }

        var type = TypeOfElement(element);
        if (type is not null && constructs)
            Roots.Constructor(type);

        var scoped = type is not null && PushesNameScope(type);
        if (scoped)
            PushNameScope(element);

        foreach (var attribute in element.Attributes())
            RootParsedAttribute(element, type, attribute);

        if (
            type is not null
            && HasContent(element)
            && resolver.FindContentProperty(type) is { } content
        )
            RootSetProperty(type, content);

        foreach (var child in element.Elements())
            RootParsedTree(child);

        if (scoped)
            _nameScopes.RemoveAt(_nameScopes.Count - 1);
    }

    static bool HasContent(XElement element) =>
        element
            .Nodes()
            .Any(n =>
                n is XElement { Name.LocalName: var name } && name.IndexOf('.') < 0
                || n is XText text && !string.IsNullOrWhiteSpace(text.Value)
            );

    // The parser sets a plain CLR property through reflection; a dependency property natively.
    IPropertySymbol? RootSetProperty(INamedTypeSymbol owner, string name)
    {
        if (resolver.FindProperty(owner, name) is not { } property)
            return null;

        if (resolver.FindDependencyPropertyOwner(owner, name) is null)
            RootProperty(property);
        else
            RootDependencyProperty(owner, name);

        Roots.EnumLiterals(property.Type);
        return property;
    }

    void RootParsedAttribute(XElement element, INamedTypeSymbol? type, XAttribute attribute)
    {
        var outer = _attribute;
        _attribute = attribute;
        try
        {
            RootParsedAttributeCore(element, type, attribute);
        }
        finally
        {
            _attribute = outer;
        }
    }

    void RootParsedAttributeCore(XElement element, INamedTypeSymbol? type, XAttribute attribute)
    {
        var ns = attribute.Name.NamespaceName;
        if (
            attribute.IsNamespaceDeclaration
            || ns == XamlTypeResolver.DirectiveNs
            || ns == XamlTypeResolver.ToolkitNs
            || ns == XNamespace.Xml.NamespaceName
        )
            return;

        var local = attribute.Name.LocalName;
        var value = attribute.Value;
        var call = XamlMarkupParser.IsMarkup(value) ? XamlMarkupParser.Parse(value) : null;

        var dot = local.IndexOf('.');
        if (dot > 0)
        {
            var name = local.Substring(dot + 1);
            var owner = ResolveIn(element, ns, local.Substring(0, dot));
            if (owner is not null)
                RootDependencyProperty(owner, name);

            if (call is not null)
                RootParsedMarkup(element, call, name);
            else if (
                owner is not null
                && resolver.FindAttachedValueType(owner, name) is { } attachedType
            )
                Roots.EnumLiterals(attachedType);
            return;
        }

        if (type is null)
        {
            if (call is not null)
                RootParsedMarkup(element, call, local);
            return;
        }

        if (IsSetterLike(type) && local is "Property" or "Value")
        {
            if (local == "Property")
                RootSetterProperty(element, value);
            else if (call is not null)
                RootParsedMarkup(element, call, null);
            return;
        }

        if (call is not null)
        {
            RootParsedMarkup(element, call, local);
            return;
        }

        RootNamedByText(element, type, local, value);

        if (RootSetProperty(type, local) is not { } property)
            return;

        switch (XamlTypeResolver.Fqn(property.Type))
        {
            case "global::System.Type" when ResolveTypeSymbol(element, value) is { } named:
                RootNamedType(named);
                break;
            case "global::Noesis.PropertyPath":
                RootPropertyPath(element, value);
                break;
        }
    }

    // Found by name, so it has to survive with its metadata; a constructor keeps both.
    void RootNamedType(INamedTypeSymbol type)
    {
        Roots.Constructor(type);
        Roots.EnumLiterals(type);
    }

    void RootParsedMarkup(XElement element, MarkupCall call, string? slot)
    {
        switch (call.Name)
        {
            case "Binding":
                RootBinding(element, call, slot);
                ReportParsedBinding(element);
                RootNestedMarkup(element, call);
                return;
            case "x:Type" or "Type":
                if (ResolveTypeSymbol(element, call.Positional.FirstOrDefault() ?? "") is { } type)
                    RootNamedType(type);
                return;
            case "TemplateBinding":
            case "StaticResource":
            case "DynamicResource":
            case "x:Null":
            case "Null":
            case "x:Static":
            case "Static":
                return;
        }

        if (
            (
                ResolveTypeSymbol(element, call.Name)
                ?? ResolveTypeSymbol(element, call.Name + "Extension")
            )
            is not { } extension
        )
            return;

        Roots.Constructor(extension);
        foreach (var pair in call.Named)
        {
            if (resolver.FindProperty(extension, pair.Key) is { } property)
                RootProperty(property);
        }

        if (call.Positional.Count > 0 && resolver.FindContentProperty(extension) is { } content)
        {
            if (resolver.FindProperty(extension, content) is { } property)
                RootProperty(property);
        }

        RootNestedMarkup(element, call);
    }

    void RootNestedMarkup(XElement element, MarkupCall call)
    {
        foreach (var pair in call.Named)
        {
            if (pair.Value is MarkupCall nested)
                RootParsedMarkup(element, nested, null);
        }

        foreach (var nested in call.PositionalCalls)
            RootParsedMarkup(element, nested, null);
    }

    void RootSetterProperty(XElement setter, string raw)
    {
        var name = raw.Trim();
        var dot = name.LastIndexOf('.');
        ITypeSymbol? valueType = null;

        if (dot > 0)
        {
            if (ResolveTypeSymbol(setter, name.Substring(0, dot)) is not { } owner)
                return;

            var property = name.Substring(dot + 1);
            RootDependencyProperty(owner, property);
            valueType = resolver.FindAttachedValueType(owner, property);
        }
        else if (SetterTarget(setter) is { } styled)
        {
            valueType = resolver.FindProperty(styled, name)?.Type;
            RootDependencyProperty(styled, name);
        }

        if (
            valueType is not null
            && setter.Attribute("Value") is { } value
            && !XamlMarkupParser.IsMarkup(value.Value)
        )
            Roots.EnumLiterals(valueType);
    }

    INamedTypeSymbol? SetterTarget(XElement setter)
    {
        if (
            setter.Attribute("TargetName")?.Value is { } named
            && LookupName(named.Trim()) is { } target
        )
            return target;

        foreach (var scope in setter.Ancestors())
        {
            if (scope.Attribute("TargetType") is { } declared)
                return ResolveTypeSymbol(scope, declared.Value);
        }

        return null;
    }

    // The loader fills the root rather than constructing it, and finds ConnectEvent by reflection.
    public void RootLoaded(XElement root, INamedTypeSymbol rootClass, bool connectsEvents)
    {
        _rootClass = rootClass;
        PushNameScope(root);

        if (connectsEvents)
            Roots.Member(rootClass, "ConnectEvent(System.Object,System.String,System.String)");

        RootParsed(root, "document-left-to-the-loader", constructs: false);
    }

    void ReportUnrooted(XElement element, string message)
    {
        // NTK1006 already names the stated type every name under it depends on.
        if (
            element
                .AncestorsAndSelf()
                .Any(scope =>
                    scope
                        .Attributes()
                        .Any(a => IsStatedType(a.Name) && ResolveTypeSymbol(scope, a.Value) is null)
                )
        )
            return;

        Unrooted.Add(SiteOf(element, message));
    }

    void ReportParsedBinding(XElement element)
    {
        if (_parsedBy is null || (_attribute is { } attribute && _heldBack.Contains(attribute)))
            return;

        LeftNative.Add(SiteOf(element, $"binding stays native: {_parsedBy}"));
    }

    MarkupSite SiteOf(XElement element, string message)
    {
        IXmlLineInfo at =
            _attribute is { } attribute && attribute.Parent == element ? attribute : element;

        return at.HasLineInfo()
            ? new MarkupSite(message, at.LineNumber, at.LinePosition)
            : new MarkupSite(message, 0, 0);
    }

    public void RootReferencedTypes(XElement root)
    {
        _referenced = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        try
        {
            foreach (var element in root.DescendantsAndSelf())
                Reference(element);

            foreach (
                var type in Roots
                    .Types.Concat(_referenced)
                    .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default)
                    .ToList()
            )
                Roots.Overrides(type);
        }
        finally
        {
            _referenced = null;
        }
    }

    void Reference(XElement element)
    {
        var local = element.Name.LocalName;
        if (local.IndexOf('.') < 0 && TypeOfElement(element) is { } type)
            _referenced!.Add(type);

        if (local == "Binding" && BindingElementCall(element) is { } call)
            ReferenceThrough(element, call, SlotAround(element));

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            var name = attribute.Name;
            if (
                IsStatedType(name)
                || name.NamespaceName.Length == 0 && name.LocalName is "DataType" or "TargetType"
            )
            {
                if (ResolveTypeSymbol(element, attribute.Value) is { } stated)
                    _referenced!.Add(stated);
                else if (IsStatedType(name))
                    UnresolvedTypes.Add(attribute);
            }
            else if (XamlMarkupParser.Parse(attribute.Value) is { Name: "Binding" } binding)
            {
                ReferenceThrough(
                    element,
                    binding,
                    name.LocalName.Substring(name.LocalName.IndexOf('.') + 1)
                );
            }
        }
    }

    static bool IsStatedType(XName name) =>
        name.NamespaceName == XamlTypeResolver.ToolkitNs
        && name.LocalName is "DataType" or "AncestorDataType" or ItemTypeAttribute;

    void ReferenceThrough(XElement element, MarkupCall call, string? slot)
    {
        if (BindingPath(call) is not { } path)
            return;

        var mark = MarkRoots();
        Quietly(() => BindingRoots(element, call, slot, path));
        RestoreRoots(mark);
    }
}
