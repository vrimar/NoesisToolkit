using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

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

        // First and in scope for the subtree: StaticResource resolves lexically outward.
        var resources = element
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal));

        var depth = _dictionaries.Count;
        if (resources is not null)
            ApplyPropertyElement(element, target, type, resources, resources.Name.LocalName);

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

                if (ReferenceEquals(child, resources))
                    continue;

                // Only the resources element, handled above, may leave its dictionary in scope.
                var before = _dictionaries.Count;
                ApplyPropertyElement(element, target, type, child, local);
                if (_dictionaries.Count > before)
                    _dictionaries.RemoveRange(before, _dictionaries.Count - before);
            }

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
                property.SetMethod is not null
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
        if (TextOf(element) is not { } value)
            return;

        if (TextConstructorOf(element, type) != TextConstructor.None)
            return;

        var (contentProperty, property) = ContentSlot(type);

        if (property is not null && IsInlineCollection(property.Type))
        {
            _lines.Add($"{target}.{contentProperty}.Add(new global::Noesis.Run({Quote(value)}));");
            return;
        }

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
        bool settable
    )
    {
        if (XamlTypeResolver.IsCollection(propertyType))
        {
            var itemType = CollectionItemType(propertyType);
            var inlines =
                itemType is not null
                && XamlTypeResolver.DerivesFrom(itemType, "global::Noesis.Inline");

            foreach (var node in Ordered(content, inlines))
            {
                if (node is XText text)
                {
                    _lines.Add(
                        $"{target}.{propertyName}.Add(new global::Noesis.Run({Quote(Collapse(text.Value))}));"
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

    /// <summary>Content in document order, keeping interleaved text only where it is meaningful.</summary>
    static IEnumerable<XNode> Ordered(List<XElement> content, bool includeText)
    {
        if (!includeText || content.Count == 0)
            return content;

        var parent = content[0].Parent;
        if (parent is null)
            return content;

        return parent
            .Nodes()
            .Where(n =>
                n is XElement element && !element.Name.LocalName.Contains(".")
                || n is XText text && text.Value.Trim().Length > 0
            );
    }

    // The parser collapses each interleaved text run and trims its edges; match that exactly.
    static string Collapse(string text) =>
        string.Join(
            " ",
            text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        );

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

            var dictionary = NextName("resources");

            if (wrapper?.Attribute("Source") is not null)
            {
                var built = EmitObject(wrapper);
                if (built is null)
                    return;

                _lines.Add($"var {dictionary} = (global::Noesis.ResourceDictionary){built};");
                _dictionaries.Add(dictionary);
            }
            else
            {
                _lines.Add($"var {dictionary} = new global::Noesis.ResourceDictionary();");
                _dictionaries.Add(dictionary);
                EmitDictionaryBody(wrapper ?? child, dictionary);
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
            if (child.Nodes().OfType<XText>().FirstOrDefault() is { } text)
            {
                var converted = ConvertValue(child, text.Value.Trim(), property.Type);
                if (converted is not null)
                    _lines.Add($"{target}.{propertyName} = {converted};");
            }

            return;
        }

        AssignChildren(
            target,
            propertyName,
            property.Type,
            elements.ToList(),
            property.SetMethod is not null
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
