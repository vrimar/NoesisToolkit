using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>Child elements and text: the content property, collections, and property elements.</summary>
sealed partial class XamlEmitter
{
    // XAML content properties the managed Noesis binding carries no [ContentProperty] for.
    static readonly (string Base, string Property)[] ContentFallbacks =
    {
        ("global::Noesis.FrameworkTemplate", "VisualTree"),
        ("global::Noesis.Panel", "Children"),
        ("global::Noesis.ItemsControl", "Items"),
        ("global::Noesis.Decorator", "Child"),
        ("global::Noesis.GradientBrush", "GradientStops"),
        ("global::Noesis.TimelineGroup", "Children"),
        ("global::Noesis.VisualStateGroup", "States"),
        ("global::Noesis.VisualState", "Storyboard"),
        ("global::Noesis.Style", "Setters"),
        ("global::Noesis.AnimationTimeline", "KeyFrames"),
        ("global::Noesis.BeginStoryboard", "Storyboard"),
        ("global::Noesis.MultiBinding", "Bindings"),
        ("global::Noesis.ContentControl", "Content"),
    };

    static readonly string[] ContentProbes =
    {
        "Actions",
        "Setters",
        "Children",
        "Items",
        "Content",
        "Child",
        "Bindings",
        "States",
        "GradientStops",
        "Columns",
        "Inlines",
        "KeyFrames",
        "Storyboard",
    };

    void ApplyChildren(XElement element, string target, INamedTypeSymbol type)
    {
        var content = new List<XElement>();

        // In scope only after its own position: StaticResource resolves where the parser meets it.
        var resources = element
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal));

        var depth = _dictionaries.Count;

        try
        {
            foreach (var child in element.Elements())
            {
                var local = child.Name.LocalName;
                if (!local.Contains("."))
                {
                    content.Add(child);
                    continue;
                }

                var before = _dictionaries.Count;
                ApplyPropertyElement(element, target, type, child, local);
                if (!ReferenceEquals(child, resources) && _dictionaries.Count > before)
                    _dictionaries.RemoveRange(before, _dictionaries.Count - before);
            }

            // Once any content precedes it, the parser applies resources after all content is built.
            if (_dictionaries.Count > depth && content.Count > 0 && content[0].IsBefore(resources!))
                _dictionaries.RemoveAt(_dictionaries.Count - 1);

            if (content.Count == 0)
            {
                ApplyTextContent(element, target, type);
                return;
            }

            var (contentProperty, property) = ContentSlot(type);
            if (property is null)
            {
                Errors.Add(
                    $"<{element.Name.LocalName}> has children but no usable content property"
                );
                return;
            }

            AssignChildren(
                target,
                contentProperty!,
                property.Type,
                content,
                property.SetMethod is not null,
                type
            );
        }
        finally
        {
            if (_dictionaries.Count > depth)
                _dictionaries.RemoveRange(depth, _dictionaries.Count - depth);
        }
    }

    /// <summary>The content property's name and symbol, which every caller needs together.</summary>
    (string? Name, IPropertySymbol? Property) ContentSlot(INamedTypeSymbol type)
    {
        var name = ContentPropertyOf(type);
        return (name, name is null ? null : resolver.FindProperty(type, name));
    }

    void ApplyTextContent(XElement element, string target, INamedTypeSymbol type)
    {
        if (TextConstructorOf(element, type) != TextConstructor.None)
            return;

        var (contentProperty, property) = ContentSlot(type);

        if (property is not null && IsInlineCollection(property.Type))
        {
            if (ContentNodes(element, type).OfType<string>().LastOrDefault() is { } run)
                _lines.Add(
                    $"{target}.{contentProperty}.Add(new global::Noesis.Run({Quote(run)}));"
                );
            return;
        }

        if (TextOf(element) is not { } value)
            return;

        var converted = property?.SetMethod is null
            ? null
            : ConvertValue(element, value, property.Type);

        if (converted is null)
        {
            DeadMarkup.Add($"<{element.Name.LocalName}> text content '{value}' has nowhere to go");
            return;
        }

        _lines.Add($"{target}.{contentProperty} = {converted};");
    }

    static bool IsInlineCollection(ITypeSymbol type) =>
        XamlTypeResolver.IsCollection(type)
        && CollectionItemType(type) is { } item
        && XamlTypeResolver.DerivesFrom(item, "global::Noesis.Inline");

    void AssignChildren(
        string target,
        string propertyName,
        ITypeSymbol propertyType,
        List<XElement> content,
        bool settable,
        INamedTypeSymbol owner
    )
    {
        if (XamlTypeResolver.IsCollection(propertyType))
        {
            var itemType = CollectionItemType(propertyType);
            var inlines =
                itemType is not null
                && XamlTypeResolver.DerivesFrom(itemType, "global::Noesis.Inline");

            IEnumerable<object> nodes =
                inlines && content[0].Parent is { } parent ? ContentNodes(parent, owner) : content;

            foreach (var node in nodes)
            {
                if (node is string run)
                {
                    _lines.Add(
                        $"{target}.{propertyName}.Add(new global::Noesis.Run({Quote(run)}));"
                    );
                    continue;
                }

                var name = EmitObject((XElement)node, itemType);
                if (name is not null)
                    _lines.Add($"{target}.{propertyName}.Add({name});");
            }

            return;
        }

        if (content.Count > 1)
        {
            Errors.Add($"'{propertyName}' takes one value but {content.Count} were given");
            return;
        }

        if (!settable)
        {
            Errors.Add($"'{propertyName}' is read-only and not a collection");
            return;
        }

        var only = EmitObject(content[0], propertyType);
        if (only is not null)
            _lines.Add($"{target}.{propertyName} = {only};");
    }

    // A null owner shapes text as a plain value, not inlines.
    IEnumerable<object> ContentNodes(XElement parent, INamedTypeSymbol? inlineOwner)
    {
        var preserve = PreservesSpace(parent);
        var significant =
            inlineOwner is not null && SpaceSignificant.Contains(XamlTypeResolver.Fqn(inlineOwner));

        XElement? previous = null;
        var text = new System.Text.StringBuilder();
        var pending = false;

        foreach (var node in parent.Nodes())
        {
            if (node is XText run)
            {
                text.Append(run.Value);
                pending = true;
                continue;
            }

            if (node is not XElement element)
                continue;

            if (pending && ShapeRun(text.ToString(), previous, element) is { } shaped)
                yield return shaped;

            text.Clear();
            pending = false;

            if (element.Name.LocalName.Contains("."))
                continue;

            previous = element;
            yield return element;
        }

        if (pending && ShapeRun(text.ToString(), previous, null) is { } last)
            yield return last;

        string? ShapeRun(string raw, XElement? before, XElement? after)
        {
            if (preserve)
                return raw.Length > 0 ? raw : null;

            var collapsed = CollapseSpace(raw);

            if (!significant)
                collapsed = collapsed.Trim(' ');
            else if (before is null || IsLineBreak(before))
                collapsed =
                    before is null && after is null
                        ? collapsed.Trim(' ')
                        : collapsed.TrimStart(' ');
            else if (after is null || IsLineBreak(after))
                collapsed = collapsed.TrimEnd(' ');

            return collapsed.Length > 0 ? collapsed : null;
        }
    }

    // Only these exact native types keep a run's edge space; subclasses and Hyperlink trim both.
    static readonly HashSet<string> SpaceSignificant = new HashSet<string>(StringComparer.Ordinal)
    {
        "global::Noesis.TextBlock",
        "global::Noesis.Span",
        "global::Noesis.Bold",
        "global::Noesis.Italic",
    };

    bool IsLineBreak(XElement element) =>
        resolver.SymbolOf(element) is { } symbol
        && XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.LineBreak");

    static bool PreservesSpace(XElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            if (e.Attribute(XNamespace.Xml + "space") is { } space)
                return space.Value == "preserve";
        }

        return false;
    }

    // XML whitespace only: a non-breaking or ideographic space is text.
    static string CollapseSpace(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var space = false;

        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                space = true;
                continue;
            }

            if (space)
                builder.Append(' ');

            space = false;
            builder.Append(c);
        }

        if (space)
            builder.Append(' ');

        return builder.ToString();
    }

    // Add(T) is declared on the generic base, so the item type is only visible up the chain.
    static ITypeSymbol? CollectionItemType(ITypeSymbol collection)
    {
        for (var t = collection; t is not null; t = t.BaseType)
        {
            var add = t.GetMembers("Add")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => !m.IsStatic && m.Parameters.Length == 1);

            if (add is not null)
                return add.Parameters[0].Type;
        }

        return null;
    }

    void ApplyPropertyElement(
        XElement owner,
        string target,
        INamedTypeSymbol type,
        XElement child,
        string local
    )
    {
        var dot = local.IndexOf('.');
        var ownerName = local.Substring(0, dot);
        var propertyName = local.Substring(dot + 1);

        var declaring = ResolveIn(owner, child.Name.NamespaceName, ownerName);

        var attached =
            declaring is not null
            && !XamlTypeResolver.DerivesFrom(type, XamlTypeResolver.Fqn(declaring))
            && resolver.FindAttachedGetter(declaring, propertyName) is not null;

        if (attached)
        {
            var declaringFqn = XamlTypeResolver.Fqn(declaring!);
            var getter = resolver.FindAttachedGetter(declaring!, propertyName)!;

            if (XamlTypeResolver.IsCollection(getter.ReturnType))
            {
                foreach (var value in child.Elements())
                {
                    var name = EmitObject(value);
                    if (name is not null)
                        _lines.Add($"{declaringFqn}.Get{propertyName}({target}).Add({name});");
                }

                return;
            }

            var values = child.Elements().ToArray();
            if (values.Length != 1)
            {
                Errors.Add($"<{local}> must contain exactly one element");
                return;
            }

            var single = EmitObject(values[0]);
            if (single is not null)
                _lines.Add($"{declaringFqn}.Set{propertyName}({target}, {single});");
            return;
        }

        var property = resolver.FindProperty(type, propertyName);
        if (property is null)
        {
            Errors.Add($"'{propertyName}' is not a property of {XamlTypeResolver.Fqn(type)}");
            return;
        }

        var elements = child.Elements().ToArray();

        if (
            propertyName == "Style"
            && elements.Length == 1
            && elements[0].Name.LocalName == "Style"
        )
            PlanCompiledTriggers(owner, target, type, elements[0]);

        if (XamlTypeResolver.DerivesFrom(property.Type, "global::Noesis.ResourceDictionary"))
        {
            // <X.Resources> either holds the entries itself or wraps them in a ResourceDictionary.
            var wrapper =
                elements.Length == 1 && elements[0].Name.LocalName == "ResourceDictionary"
                    ? elements[0]
                    : null;

            // The parser rejects a keyed wrapper and leaves the element's dictionary as it was.
            if (wrapper?.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs)) is not null)
                return;

            var dictionary = NextName("resources");

            if (wrapper?.Attribute("Source") is not null)
            {
                var built = EmitObject(wrapper);
                if (built is null)
                    return;

                _lines.Add($"var {dictionary} = (global::Noesis.ResourceDictionary){built};");
                _dictionaries.Add(dictionary);
            }
            else if (wrapper is not null)
            {
                _lines.Add($"var {dictionary} = new global::Noesis.ResourceDictionary();");
                _dictionaries.Add(dictionary);
                EmitDictionaryBody(wrapper, dictionary);
            }
            else
            {
                // Bare entries join the dictionary the element already holds rather than replace it.
                _lines.Add(
                    property.SetMethod is null
                        ? $"var {dictionary} = {target}.{propertyName};"
                        : $"var {dictionary} = {target}.{propertyName} ?? ({target}.{propertyName} = new global::Noesis.ResourceDictionary());"
                );
                _dictionaries.Add(dictionary);
                EmitDictionaryBody(child, dictionary);
                return;
            }

            _lines.Add($"{target}.{propertyName} = {dictionary};");
            return;
        }

        if (elements.Length == 1 && elements[0].Name.LocalName is "MultiBinding" or "Binding")
        {
            if (
                elements[0].Name.LocalName == "MultiBinding"
                    ? PlainSlot(property) is { } slot
                        && TryEmitCompiledMultiBinding(owner, target, type, slot, elements[0])
                    : TryEmitCompiledBindingElement(owner, target, type, property, elements[0])
            )
                return;

            var binding =
                elements[0].Name.LocalName == "MultiBinding"
                    ? EmitMultiBinding(elements[0])
                    : EmitBindingElement(elements[0]);

            if (binding is null)
                return;

            if (BindStatement(target, type, property, binding) is { } statement)
                _lines.Add(statement);
            return;
        }

        if (elements.Length == 0)
        {
            if (IsInlineCollection(property.Type))
            {
                if (ContentNodes(child, type).OfType<string>().LastOrDefault() is { } run)
                    _lines.Add(
                        $"{target}.{propertyName}.Add(new global::Noesis.Run({Quote(run)}));"
                    );

                return;
            }

            if (TextOf(child) is not { } text)
                return;

            // Converted against the type Property names, as the attribute form is.
            if (IsSetterLike(type) && propertyName == "Value" && !XamlMarkupParser.IsMarkup(text))
            {
                ApplySetterValue(child, target, text);
                return;
            }

            var converted = ConvertValue(child, text, property.Type);
            if (converted is not null)
                _lines.Add($"{target}.{propertyName} = {converted};");

            return;
        }

        AssignChildren(
            target,
            propertyName,
            property.Type,
            elements.ToList(),
            property.SetMethod is not null,
            type
        );
    }

    string? ContentPropertyOf(INamedTypeSymbol type)
    {
        if (resolver.FindContentProperty(type) is { } declared)
            return declared;

        foreach (var (baseName, property) in ContentFallbacks)
        {
            if (
                XamlTypeResolver.DerivesFrom(type, baseName)
                && resolver.FindProperty(type, property) is not null
            )
                return property;
        }

        foreach (var probe in ContentProbes)
        {
            if (resolver.FindProperty(type, probe) is not null)
                return probe;
        }

        return null;
    }
}
