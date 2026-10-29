using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>The handful of constructs the compiler hands back to the XAML parser, and the
/// pieces held back from those fragments because a parser running alone cannot resolve them.</summary>
sealed partial class XamlEmitter
{
    static readonly string[] LiveValueExtensions =
    {
        "Binding",
        "MultiBinding",
        "TemplateBinding",
        "DynamicResource",
    };

    static bool NeedsParsedSetter(XElement element, INamedTypeSymbol type)
    {
        if (type.Name != "Setter")
            return false;

        var valueElements = element
            .Elements()
            .Where(e => e.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal))
            .ToList();

        // Only the Setter's own value decides; a whole template below it is not the parser's to take.
        if (valueElements.Count > 0)
            return valueElements
                .SelectMany(e => e.Elements())
                .Any(e => e.Name.LocalName is "Binding" or "MultiBinding" or "DynamicResource");

        var value = element.Attribute("Value")?.Value;
        if (value is null || !XamlMarkupParser.IsMarkup(value))
            return false;

        var call = XamlMarkupParser.Parse(value);
        return call is not null && LiveValueExtensions.Contains(call.Name);
    }

    const string SetterLeftToParser = "setter-left-to-the-parser";

    /// <summary>Re-parses one Setter inside a probe Style, so its Property resolves and its
    /// value becomes the expression the parser would have produced.</summary>
    string? EmitSetterProbe(XElement setter)
    {
        var declared = setter
            .AncestorsAndSelf()
            .Select(e => e.Attribute("TargetType"))
            .FirstOrDefault(a => a is not null)
            ?.Value;

        var scopeNamespace = (string?)null;
        var attached = setter.Attribute("Property")?.Value.Contains('.') == true;
        var named = setter.Attribute("TargetName")?.Value;

        // A named target resolves Property against its own type, not the template's.
        if (!attached && named is not null && LookupNamed(named) is { } target)
        {
            var ns =
                target.Name.NamespaceName.Length == 0
                    ? DefaultNamespace(target)
                    : target.Name.NamespaceName;

            // Rebinding a namespace the fragment already maps would re-prefix its own elements.
            string prefix;
            if (ns == DefaultNamespace(setter))
                prefix = "";
            else if (setter.GetPrefixOfNamespace(ns) is { } bound)
                prefix = bound + ":";
            else
            {
                prefix = "__probe:";
                scopeNamespace = ns;
            }

            declared = "{x:Type " + prefix + target.Name.LocalName + "}";
        }
        else if (declared is null && !attached)
        {
            return Fail("a Setter with a live value needs a TargetType or TargetName in scope");
        }

        var style = StyleProbe(declared, new XElement(setter));
        var probe = Probe(setter, style);
        RootParsed(setter, SetterLeftToParser);

        if (scopeNamespace is not null)
            probe.Add(new XAttribute(XNamespace.Xmlns + "__probe", scopeNamespace));

        if (
            Carry(
                setter,
                probe,
                ResourceKeysIn(style),
                SetterLeftToParser,
                "a Setter left to the parser"
            )
            is null
        )
            return null;

        var name = NextName("setter");
        _lines.Add($"var {name} = (global::Noesis.Setter){ParsedStyle(probe)}.Setters[0];");

        return name;
    }

    // Noesis seals what it parses: carry every named declaration, transitively, in document order.
    List<string>? Carry(
        XElement scope,
        XElement probe,
        IEnumerable<string> keys,
        string reason,
        string what
    )
    {
        var pending = new Queue<(string Key, XElement From)>(keys.Select(k => (k, scope)));
        var carried = new List<string>();
        var declarations = new List<XElement>();
        while (pending.Count > 0)
        {
            var (key, from) = pending.Dequeue();
            if (carried.Contains(key))
                continue;

            carried.Add(key);
            if (DeclarationOf(from, key) is not { } declaration)
            {
                Errors.Add($"{what} names '{key}', which its own file does not declare");
                return null;
            }

            declarations.Add(declaration);
            RootParsed(declaration, reason);

            foreach (var inner in ResourceKeysIn(declaration))
                pending.Enqueue((inner, declaration));
        }

        probe.AddFirst(
            declarations
                .OrderBy(d => (XNode)d, XNode.DocumentOrderComparer)
                .Select(d => new XElement(d))
                .ToArray()
        );
        return carried;
    }

    static IEnumerable<string> ResourceKeysIn(XElement fragment) =>
        fragment
            .DescendantsAndSelf()
            .SelectMany(e =>
                e.Name.LocalName == "StaticResource"
                    ? StaticResourceElementKey(e)
                    : e.Attributes()
                        .Where(a => !a.IsNamespaceDeclaration)
                        .SelectMany(a => StaticResourceCalls(a.Value))
                        .Select(StaticResourceKey)
                        .OfType<string>()
            )
            .Distinct();

    static IEnumerable<string> StaticResourceElementKey(XElement element) =>
        element.Attribute("ResourceKey")?.Value.Trim() is { Length: > 0 } key
            ? new[] { key }
            : Enumerable.Empty<string>();

    static string? StaticResourceKey(MarkupCall call) =>
        (
            call.Positional.FirstOrDefault()
            ?? call.Named.FirstOrDefault(p => p.Key == "ResourceKey").Value as string
        )?.Trim();

    static IEnumerable<MarkupCall> StaticResourceCalls(string value) =>
        XamlMarkupParser.IsMarkup(value) && XamlMarkupParser.Parse(value) is { } call
            ? StaticResourceCalls(call)
            : Enumerable.Empty<MarkupCall>();

    static IEnumerable<MarkupCall> StaticResourceCalls(MarkupCall call)
    {
        if (call.Name == "StaticResource")
            yield return call;

        var nested = call
            .Named.Select(p => p.Value)
            .OfType<MarkupCall>()
            .Concat(call.PositionalCalls);
        foreach (var inner in nested)
        {
            foreach (var found in StaticResourceCalls(inner))
                yield return found;
        }
    }

    // The parser resolves a key from where it is written outward, so the nearest declaration wins.
    static XElement? DeclarationOf(XElement scope, string key)
    {
        foreach (var holder in scope.AncestorsAndSelf())
        {
            var entries =
                holder.Name.LocalName == "ResourceDictionary"
                    ? holder.Elements()
                    : holder
                        .Elements()
                        .Where(e =>
                            e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)
                        )
                        .SelectMany(r => r.Elements())
                        .SelectMany(e =>
                            e.Name.LocalName == "ResourceDictionary" ? e.Elements() : new[] { e }
                        );

            if (
                entries.FirstOrDefault(e =>
                    e.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs))?.Value == key
                ) is
                { } declaration
            )
                return declaration;
        }

        return DeclaredIn(scope.Document?.Root, key);
    }

    static XElement? DeclaredIn(XElement? root, string key) =>
        root
            ?.DescendantsAndSelf()
            .FirstOrDefault(e =>
                e.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs))?.Value == key
            );

    /// <summary>Noesis types a few slots as BindingExpressionBase, which no managed value can be
    /// assigned to, and stores a few value types managed code writes as pointers; that element has
    /// to stay on the XAML parser.</summary>
    bool HasUnassignableSlot(XElement element, INamedTypeSymbol type) =>
        Unassignable(element, type, resolver)
        || HasMisroutedAttachedWrite(element)
        || !IsSetterLike(type)
            && element
                .Attributes()
                .Any(a =>
                    !a.IsNamespaceDeclaration
                    && XamlMarkupParser.IsMarkup(a.Value)
                    && XamlMarkupParser.Parse(a.Value) is { } call
                    && HasUnconstructibleExtension(element, call)
                );

    bool HoldsLiveReference(XElement element, INamedTypeSymbol type) =>
        LiveReferenceIn(element, type, resolver) is not null;

    // Managed code makes a resource reference live on an element only; the parser, on what it holds.
    internal static XAttribute? LiveReferenceIn(
        XElement element,
        INamedTypeSymbol type,
        XamlTypeResolver resolver
    )
    {
        if (!XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement"))
            return null;

        var pending = new Stack<XElement>(element.Elements());
        while (pending.Count > 0)
        {
            var child = pending.Pop();
            if (!child.Name.LocalName.Contains("."))
            {
                if (
                    resolver.SymbolOf(child) is not { } symbol
                    || !XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.DependencyObject")
                    || XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkElement")
                    || XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate")
                    || XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.Style")
                    || XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.ResourceDictionary")
                )
                    continue;

                if (
                    child
                        .Attributes()
                        .FirstOrDefault(a =>
                            !a.IsNamespaceDeclaration
                            && XamlMarkupParser.IsMarkup(a.Value)
                            && XamlMarkupParser.Parse(a.Value) is { Name: "DynamicResource" }
                        ) is
                    { } live
                )
                    return live;
            }

            foreach (var grandchild in child.Elements())
                pending.Push(grandchild);
        }

        return null;
    }

    internal static bool Unassignable(
        XElement element,
        INamedTypeSymbol type,
        XamlTypeResolver resolver
    )
    {
        foreach (var attribute in element.Attributes())
        {
            var ns = attribute.Name.NamespaceName;
            if (
                attribute.IsNamespaceDeclaration
                || attribute.Name.LocalName.Contains(".")
                || ns.Length > 0 && ns != element.Name.NamespaceName
                || IsIgnorable(element, attribute)
            )
                continue;

            var name = attribute.Name.LocalName;
            if (
                UnassignableSlot(resolver.FindProperty(type, name), attribute.Value)
                || Wrapperless(type, name, resolver)
            )
                return true;
        }

        foreach (var child in element.Elements())
        {
            var local = child.Name.LocalName;
            var dot = local.IndexOf('.');
            if (dot < 0)
                continue;

            var name = local.Substring(dot + 1);
            var property = resolver.FindProperty(type, name);
            if (
                Wrapperless(type, name, resolver)
                || (
                    child.HasElements
                        ? property is not null && MisroutedSetter(property.Type, property)
                        : UnassignableSlot(property, child.Value)
                )
            )
                return true;
        }

        return false;
    }

    // Generated code types and writes a value through the CLR property, which these lack.
    static bool Wrapperless(ITypeSymbol type, string name, XamlTypeResolver resolver) =>
        resolver.FindProperty(type, name) is null
        && resolver.FindEvent(type, name) is null
        && resolver.FindDependencyPropertyOwner(type, name) is not null;

    bool SetterNamesWrapperless(XElement element, INamedTypeSymbol type)
    {
        if (type.Name != "Setter" || element.Attribute("Property")?.Value.Trim() is not { } name)
            return false;

        var dot = name.LastIndexOf('.');
        if (dot > 0)
            return ResolveTypeSymbol(element, name.Substring(0, dot)) is { } owner
                && resolver.FindAttachedSetter(owner, name.Substring(dot + 1)) is null
                && Wrapperless(owner, name.Substring(dot + 1), resolver);

        var styled = StyledBy(element);
        return styled is not null
            && resolver.FindAttachedValueType(styled, name) is null
            && Wrapperless(styled, name, resolver);
    }

    INamedTypeSymbol? StyledBy(XElement setter) =>
        setter.Attribute("TargetName")?.Value is { } named ? LookupName(named) : StyleTarget;

    bool SetterValueUnconvertible(XElement element, INamedTypeSymbol type) =>
        type.Name == "Setter"
        && SetterLiteral(element) is not null
        && SetterValueType(element) is { } valueType
        && Unconvertible(valueType);

    static string? SetterLiteral(XElement setter)
    {
        if (setter.Attribute("Value")?.Value is { } value)
            return XamlMarkupParser.IsMarkup(value) ? null : value;

        return
            setter
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal))
                is { HasElements: false } child
            ? child.Value
            : null;
    }

    ITypeSymbol? SetterValueType(XElement setter)
    {
        if (setter.Attribute("Property")?.Value.Trim() is not { } name)
            return null;

        var dot = name.LastIndexOf('.');
        if (dot > 0)
            return ResolveTypeSymbol(setter, name.Substring(0, dot)) is { } owner
                ? AttachedValueTypeOf(owner, name.Substring(dot + 1))
                : null;

        return StyledBy(setter) is { } styled
            ? resolver.FindProperty(styled, name)?.Type
                ?? resolver.FindAttachedValueType(styled, name)
            : null;
    }

    static bool UnassignableSlot(IPropertySymbol? property, string value) =>
        property is not null
        && (
            XamlTypeResolver.Fqn(property.Type)
                is "global::Noesis.BindingExpressionBase"
                    or "global::Noesis.PathFigureCollection"
            || MisroutedSetter(property.Type, property)
            || !XamlMarkupParser.IsMarkup(value) && Unconvertible(property.Type)
        );

    // Any accessor but Noesis' own writes a misrouted slot through SetValue.
    static bool MisroutedSetter(ITypeSymbol type, ISymbol owner) =>
        Misrouted(type) && owner.ContainingAssembly?.Name != "Noesis.GUI";

    static bool Unconvertible(ITypeSymbol type) =>
        Misrouted(type) && !XamlTypeResolver.HasStringParse(Wrapped(type) ?? type);

    bool HasMisroutedAttachedWrite(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            var local = attribute.Name.LocalName;
            var dot = local.IndexOf('.');
            if (attribute.IsNamespaceDeclaration || dot < 0 || IsIgnorable(element, attribute))
                continue;

            if (
                ResolveIn(element, attribute.Name.NamespaceName, local.Substring(0, dot))
                    is { } owner
                && (
                    resolver.FindAttachedValueType(owner, local.Substring(dot + 1)) is { } value
                        && (
                            MisroutedSetter(value, owner)
                            || !XamlMarkupParser.IsMarkup(attribute.Value) && Unconvertible(value)
                        )
                    || WrapperUnwritable(owner, local.Substring(dot + 1), attribute.Value)
                )
            )
                return true;
        }

        return false;
    }

    // Only the parser can apply these attributes, so it builds each carrier from them alone, in a same-kind template.
    void PlanTemplateBindings(XElement scope, XElement? template)
    {
        var carried = new List<XElement>();
        foreach (var element in OwnContent(scope))
        {
            if (ParserBuilt(element).Any() && Graftable(element))
                carried.Add(element);
        }

        if (carried.Count == 0)
            return;

        var carriers = new XElement(XName.Get("Grid", XamlTypeResolver.PresentationNs));
        foreach (var element in carried)
        {
            RootCarried(element);

            var shell = new XElement(
                element.Name,
                ParserBuilt(element).Select(a => new XAttribute(a))
            );
            if (TextConstructorOf(element, resolver.SymbolOf(element)!) != TextConstructor.None)
                shell.Add(TextOf(element));

            CopyScope(shell, element);
            carriers.Add(Carrier(shell));
        }

        var probe = CarrierProbe(scope, template, carriers);

        var parts = NextName("templated");
        _lines.Add(
            $"var {parts} = __templated(global::Noesis.GUI.ParseXaml({Quote(Serialize(probe))}), {carried.Count});"
        );
        _usesTemplated = true;

        for (var i = 0; i < carried.Count; i++)
            _grafts[carried[i]] = $"{parts}[{i}]";
    }

    static XElement Carrier(XElement content) =>
        new XElement(
            XName.Get("ContentControl", XamlTypeResolver.PresentationNs),
            new XElement(XName.Get("ContentControl.Tag", XamlTypeResolver.PresentationNs), content)
        );

    static XElement CarrierProbe(XElement scope, XElement? template, XElement carriers)
    {
        var probe = template is null
            ? carriers
            : new XElement(template.Name, TargetTypeOf(template), carriers);
        CopyScope(probe, scope);
        return probe;
    }

    void RootCarried(XElement element)
    {
        if (resolver.SymbolOf(element) is { } type)
            Roots.Constructor(type);

        foreach (var attribute in TemplateBindingsOf(element))
        {
            var local = attribute.Name.LocalName;
            var dot = local.IndexOf('.');
            if (
                dot > 0
                && ResolveIn(element, attribute.Name.NamespaceName, local.Substring(0, dot))
                    is { } owner
            )
                RootDependencyProperty(owner, local.Substring(dot + 1));
        }
    }

    readonly Dictionary<XElement, string> _grafts = new Dictionary<XElement, string>();

    readonly HashSet<XElement> _grafted = new HashSet<XElement>();

    readonly JournaledSet<XAttribute> _heldBack = new JournaledSet<XAttribute>();

    bool _usesTemplated;

    const string TemplatedHelper =
        "static object[] __templated(object parsed, int count)\n"
        + "{\n"
        + "    var carriers = (global::Noesis.Panel)(parsed is global::Noesis.FrameworkTemplate t ? t.VisualTree : parsed);\n"
        + "    var parts = new object[count];\n"
        + "    for (var i = 0; i < count; i++)\n"
        + "    {\n"
        + "        var carrier = (global::Noesis.ContentControl)carriers.Children[i];\n"
        + "        parts[i] = carrier.Tag;\n"
        + "        carrier.ClearValue(global::Noesis.FrameworkElement.TagProperty);\n"
        + "    }\n"
        + "    return parts;\n"
        + "}";

    static bool IsTemplateBinding(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration
        && attribute.Name.NamespaceName != XamlTypeResolver.DirectiveNs
        && XamlMarkupParser.IsMarkup(attribute.Value)
        && XamlMarkupParser.Parse(attribute.Value) is { Name: "TemplateBinding" };

    static IEnumerable<XAttribute> TemplateBindingsOf(XElement element) =>
        element.Attributes().Where(IsTemplateBinding);

    static IEnumerable<XAttribute> ParserBuilt(XElement element) =>
        element.Attributes().Where(a => IsTemplateBinding(a) || ParserSets(element, a));

    static bool ParserSets(XElement element, XAttribute attribute) =>
        XamlTypeResolver.NativeOwned(attribute.Name.NamespaceName)
        && !IsIgnorable(element, attribute)
        && !NamesAResource(attribute.Value);

    // A nested template is planned when it is built; a property element is not an object.
    IEnumerable<XElement> OwnContent(XElement scope)
    {
        var pending = new Stack<XElement>(scope.Elements().Reverse());
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            var symbol = resolver.SymbolOf(element);
            if (!element.Name.LocalName.Contains("."))
                yield return element;

            if (
                symbol is not null
                && XamlTypeResolver.DerivesFrom(symbol, "global::Noesis.FrameworkTemplate")
            )
                continue;

            foreach (var child in element.Elements().Reverse())
                pending.Push(child);
        }
    }

    // Mirrors the constructing path: any element built another way would leave its instance unused.
    bool Graftable(XElement element) =>
        element.Name.LocalName is not ("StaticResource" or "DynamicResource")
        && !(
            element.Name.LocalName == "ResourceDictionary"
            && element.Attribute("Source") is not null
        )
        && resolver.SymbolOf(element) is { } symbol
        && !IsSetterLike(symbol)
        && !HasUnassignableSlot(element, symbol)
        && !HoldsLiveReference(element, symbol)
        && !NeedsParsedSetter(element, symbol)
        && !(
            !element.HasElements && IsLiteralType(symbol) && LiteralOf(element, symbol) is not null
        )
        && !symbol.IsAbstract
        && resolver.HasConstructor(symbol);

    // A ControlTemplate set through a Style's Template setter resolves its bindings against the Style's type.
    static XAttribute? TargetTypeOf(XElement template) =>
        (template.Attribute("TargetType") is null ? SetterStyle(template) : template)?.Attribute(
            "TargetType"
        )
            is { } target
            ? new XAttribute(target)
            : null;

    /// <summary>Some Noesis types exist only natively and have no managed class to construct,
    /// so that subtree stays on the XAML parser.</summary>
    string? EmitParserFallback(XElement element, ITypeSymbol? expected)
    {
        var clone = new XElement(element);

        clone.Attribute(XName.Get("Key", XamlTypeResolver.DirectiveNs))?.Remove();

        CopyScope(clone, element);

        var parsedType = resolver.SymbolOf(element);
        var deferred = new List<XAttribute>();
        foreach (var attribute in clone.Attributes().ToArray())
        {
            if (attribute.IsNamespaceDeclaration || !NamesAResource(attribute.Value))
                continue;

            if (parsedType is null || !resolver.IsAccessible(parsedType, _rootClass))
            {
                return Fail(
                    $"<{element.Name.LocalName}> stays on the parser and its {attribute.Name.LocalName} "
                        + "names a resource, but generated code cannot name the type to assign it after"
                );
            }

            // The clone's copy has no line info; applying it would report at the element.
            var held = element.Attribute(attribute.Name)!;
            deferred.Add(held);
            _heldBack.Add(held);
            attribute.Remove();
        }

        var undeclared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descendant in clone.Descendants())
        {
            foreach (var attribute in descendant.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                var keys =
                    descendant.Name.LocalName == "StaticResource"
                    && attribute.Name.LocalName == "ResourceKey"
                        ? StaticResourceElementKey(descendant)
                        : StaticResourceCalls(attribute.Value)
                            .Select(StaticResourceKey)
                            .OfType<string>();

                foreach (var key in keys)
                {
                    if (DeclaredIn(clone, key) is null && undeclared.Add(key))
                        Errors.Add(
                            $"<{descendant.Name.LocalName} {attribute.Name.LocalName}> names "
                                + $"'{key}' inside <{element.Name.LocalName}>, which the parser "
                                + "resolves alone and that subtree does not declare"
                        );
                }
            }
        }

        var cast = CastTypeOr(expected, "global::Noesis.BaseComponent");
        RootParsed(element, "element-left-to-the-parser");

        var name = NextName(element.Name.LocalName);

        // Outside a template of its own kind the parser leaves a template binding unset.
        if (
            _templates.Count > 0
            && TemplateOwner(element) is { } template
            && (
                TemplateBindingsOf(element).Any()
                || OwnContent(element).Any(e => TemplateBindingsOf(e).Any())
            )
        )
        {
            var probe = NextName("templated");
            var carriers = new XElement(
                XName.Get("Grid", XamlTypeResolver.PresentationNs),
                Carrier(clone)
            );
            _lines.Add(
                $"var {probe} = (global::Noesis.FrameworkTemplate)global::Noesis.GUI.ParseXaml({Quote(Serialize(CarrierProbe(template, template, carriers)))});"
            );
            _lines.Add($"var {name} = ({cast})__templated({probe}, 1)[0];");
            _usesTemplated = true;
            RegisterParsedNames(element, name, probe);
        }
        else
        {
            _lines.Add(
                $"var {name} = ({cast})global::Noesis.GUI.ParseXaml({Quote(Serialize(clone))});"
            );
            RegisterParsedNames(
                element,
                name,
                $"global::Noesis.NameScope.GetNameScope((global::Noesis.DependencyObject)(object){name})"
            );

            // Parsed alone, the fragment root opens a scope the document's own parse never gives it.
            var scoped = NextName("scoped");
            _lines.Add(
                $"if ((object){name} is global::Noesis.DependencyObject {scoped}) global::Noesis.NameScope.SetNameScope({scoped}, null);"
            );
        }

        if (deferred.Count == 0)
            return name;

        var typed = NextName(element.Name.LocalName);
        _lines.Add($"var {typed} = ({XamlTypeResolver.Fqn(parsedType!)})(object){name};");
        foreach (var attribute in deferred)
            ApplyAttribute(element, typed, parsedType!, attribute);

        return name;
    }

    // A parsed fragment registers its names into its own scope, which the document never reaches.
    void RegisterParsedNames(XElement fragment, string parsed, string names)
    {
        if (_templates.Count == 0 && _rootClass is null)
            return;

        var named = fragment
            .DescendantsAndSelf()
            .Where(e =>
                !e.Ancestors()
                    .TakeWhile(a => a != fragment.Parent)
                    .Any(a =>
                        resolver.SymbolOf(a) is { } owner
                        && XamlTypeResolver.DerivesFrom(owner, "global::Noesis.FrameworkTemplate")
                    )
            )
            .Select(e => (Element: e, Name: ScopedName(e)))
            .Where(n => n.Name is not null)
            .ToList();

        if (named.Count == 0)
            return;

        var scope = NextName("parsedscope");
        _lines.Add($"var {scope} = {names};");

        foreach (var (element, name) in named)
        {
            var found = element == fragment ? parsed : $"{scope}.FindName({Quote(name!)})";

            if (_templates.Count > 0)
            {
                _lines.Add(
                    $"{_templates[_templates.Count - 1]}.RegisterName({Quote(name!)}, {found});"
                );
                continue;
            }

            if (
                element.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs)) is not null
                && resolver.SymbolOf(element) is { } symbol
            )
            {
                var field = $"this.{XamlCodeBehind.Field(name!)}";
                _lines.Add(
                    $"{field} = ({XamlTypeResolver.Fqn(resolver.Nameable(symbol, _rootClass))}){found};"
                );
                found = field;
            }

            _lines.Add($"{RootScope}.RegisterName({Quote(name!)}, {found});");
        }
    }

    static XElement StyleProbe(string? targetType, object content) =>
        new XElement(
            XName.Get("Style", XamlTypeResolver.PresentationNs),
            new XAttribute(XName.Get("Key", XamlTypeResolver.DirectiveNs), "__p"),
            targetType is null ? null : new XAttribute("TargetType", targetType),
            content
        );

    // DynamicResource is exempt: it resolves against the live tree, long after this parse.
    static bool NamesAResource(string value) => StaticResourceCalls(value).Any();

    static IEnumerable<XAttribute> Namespaces(XElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            foreach (var attribute in e.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    yield return attribute;
            }
        }
    }

    // A fragment the parser sees alone carries no enclosing scope, so every xmlns it names and the xml:space it inherits travel.
    static void CopyScope(XElement into, XElement from)
    {
        foreach (var declaration in Namespaces(from))
        {
            if (into.Attribute(declaration.Name) is null)
                into.Add(new XAttribute(declaration.Name, declaration.Value));
        }

        if (into.Attribute(XNamespace.Xml + "space") is null && PreservesSpace(from))
            into.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
    }

    /// <summary>A fragment the parser resolves alone: wrapped in a dictionary that carries its
    /// namespaces, keyed so the caller can read the one entry back out.</summary>
    static XElement Probe(XElement scope, params object?[] content)
    {
        var probe = new XElement(
            XName.Get("ResourceDictionary", XamlTypeResolver.PresentationNs),
            content
        );
        CopyScope(probe, scope);
        return probe;
    }

    static string ParsedStyle(XElement probe) =>
        $"((global::Noesis.Style)((global::Noesis.ResourceDictionary)"
        + $"global::Noesis.GUI.ParseXaml({Quote(Serialize(probe))}))[\"__p\"])";

    static string Serialize(XElement fragment)
    {
        var copy = new XElement(fragment);

        // One space, not none: the parser keeps a single space between adjacent inlines.
        foreach (var text in copy.DescendantNodes().OfType<XText>().ToList())
        {
            if (
                text is not XCData
                && CollapseSpace(text.Value) == " "
                && !PreservesSpace(text.Parent!)
            )
                text.Value = " ";
        }

        // Build-time only: the parser reads ntk:ItemType as an unknown member and ntk:DataType as the real one.
        foreach (var element in copy.DescendantsAndSelf())
            element
                .Attributes()
                .Where(a => a.Name.NamespaceName == XamlTypeResolver.ToolkitNs)
                .Remove();

        foreach (var element in copy.DescendantsAndSelf().ToList())
        {
            foreach (
                var declaration in element
                    .Attributes()
                    .Where(a => a.IsNamespaceDeclaration)
                    .ToList()
            )
            {
                if (InheritedUnchanged(element.Parent, declaration) || !Named(element, declaration))
                    declaration.Remove();
            }
        }

        return copy.ToString(SaveOptions.DisableFormatting);
    }

    static bool InheritedUnchanged(XElement? parent, XAttribute declaration)
    {
        for (var e = parent; e is not null; e = e.Parent)
        {
            if (e.Attribute(declaration.Name) is { } outer)
                return outer.Value == declaration.Value;
        }

        return false;
    }

    // Unprefixed type names in values resolve against the default namespace, so it always stays.
    static bool Named(XElement scope, XAttribute declaration)
    {
        if (declaration.Name.Namespace != XNamespace.Xmlns)
            return true;

        var uri = declaration.Value;
        var token = declaration.Name.LocalName + ":";
        foreach (var element in scope.DescendantsAndSelf())
        {
            if (element.Name.NamespaceName == uri)
                return true;

            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                    continue;

                if (
                    attribute.Name.NamespaceName == uri
                    || NamesPrefix(attribute.Value, token)
                    || ListsPrefix(attribute, declaration.Name.LocalName)
                )
                    return true;
            }

            foreach (var text in element.Nodes().OfType<XText>())
            {
                if (NamesPrefix(text.Value, token))
                    return true;
            }
        }

        return false;
    }

    static bool NamesPrefix(string value, string token)
    {
        for (
            var at = value.IndexOf(token, StringComparison.Ordinal);
            at >= 0;
            at = value.IndexOf(token, at + 1, StringComparison.Ordinal)
        )
        {
            if (
                at == 0
                || !(char.IsLetterOrDigit(value[at - 1]) || value[at - 1] is '_' or '.' or '-')
            )
                return true;
        }

        return false;
    }

    static bool ListsPrefix(XAttribute attribute, string prefix) =>
        attribute.Name.NamespaceName == XamlTypeResolver.CompatibilityNs
        && attribute.Name.LocalName is "Ignorable" or "MustUnderstand"
        && attribute
            .Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Contains(prefix);

    /// <summary>Re-parses just this Style's BasedOn reference, so a theme style still resolves.</summary>
    string BasedOnProbe(XElement style)
    {
        var probe = Probe(
            style,
            StyleProbe(
                style.Attribute("TargetType")?.Value,
                new XAttribute("BasedOn", style.Attribute("BasedOn")!.Value)
            )
        );

        return ParsedStyle(probe) + ".BasedOn";
    }
}
