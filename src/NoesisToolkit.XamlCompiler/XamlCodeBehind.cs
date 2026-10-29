using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

/// <summary>
/// The members an <c>x:Class</c> document contributes to its partial class: the loader entry point,
/// one accessor per <c>x:Name</c>, and the event bridge the native loader calls back through.
/// </summary>
static class XamlCodeBehind
{
    internal readonly record struct NamedElement(
        string Name,
        string TypeFqn,
        string Modifier,
        XAttribute Declared
    );

    internal readonly record struct EventHook(
        string EventName,
        string HandlerName,
        string OwnerFqn,
        string? RoutedEvent = null,
        string? HandlerType = null
    );

    internal sealed class Scan
    {
        public readonly List<NamedElement> Names = new List<NamedElement>();
        public readonly List<EventHook> Events = new List<EventHook>();

        public bool UnresolvedHandlers;

        public readonly List<XAttribute> Unwireable = new List<XAttribute>();

        public readonly List<MarkupSite> Problems = new List<MarkupSite>();

        public IXmlLineInfo ClassSite = null!;

        public XAttribute? LoaderAttribute;

        public XAttribute? LiveResource;

        public bool NeedsLoader =>
            Events.Count > 0 || UnresolvedHandlers || LiveResource is not null;
    }

    /// <summary>
    /// Types every <c>x:Name</c> against the compilation rather than its XML tag: <c>&lt;MenuItem&gt;</c>
    /// is <c>Noesis.MenuItem</c>, which the tag name alone would resolve in the code-behind's namespace.
    /// </summary>
    public static Scan Read(XElement root, INamedTypeSymbol rootClass, XamlTypeResolver resolver)
    {
        var scan = new Scan
        {
            ClassSite =
                (IXmlLineInfo?)root.Attribute(XName.Get("Class", XamlTypeResolver.DirectiveNs))
                ?? root,
        };

        if (
            root.Attribute(XName.Get("ClassModifier", XamlTypeResolver.DirectiveNs)) is { } modifier
        )
        {
            if (Stated(modifier.Value) is not { } stated)
                scan.Problems.Add(
                    At(
                        modifier,
                        $"x:ClassModifier='{modifier.Value}' is not an accessibility C# can declare"
                    )
                );
            else if (stated != rootClass.DeclaredAccessibility)
                scan.Problems.Add(
                    At(
                        modifier,
                        $"x:ClassModifier='{modifier.Value}' disagrees with how {rootClass.Name} is declared"
                    )
                );
        }

        foreach (var element in root.DescendantsAndSelf())
        {
            var type = resolver.SymbolOf(element);

            if (type is null)
            {
                // The loader binds what the compiler cannot, so a suspected handler is enough.
                if (
                    element
                        .Attributes()
                        .FirstOrDefault(a =>
                            IsHandlerShaped(a) && DeclaresMethod(rootClass, a.Value)
                            || AttachedEvent(element, a, resolver) is not null
                        ) is
                    { } suspected
                )
                {
                    scan.UnresolvedHandlers = true;
                    scan.Unwireable.Add(suspected);
                    scan.LoaderAttribute ??= suspected;
                }
                continue;
            }

            var inTemplate = InTemplate(element, resolver);

            foreach (var attribute in element.Attributes())
            {
                var local = attribute.Name.LocalName;

                if (attribute.Name.NamespaceName == XamlTypeResolver.DirectiveNs)
                {
                    // A template registers into its own scope, which a field could never read.
                    if (local == "Name" && !inTemplate)
                    {
                        if (
                            !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(
                                attribute.Value
                            )
                        )
                            scan.Problems.Add(
                                At(
                                    attribute,
                                    $"x:Name='{attribute.Value}' is not a C# identifier, so no field can be generated for it"
                                )
                            );
                        else
                            scan.Names.Add(
                                new NamedElement(
                                    attribute.Value,
                                    XamlTypeResolver.Fqn(resolver.Nameable(type, rootClass)),
                                    Modifier(
                                        element,
                                        resolver.Nameable(type, rootClass),
                                        rootClass,
                                        scan
                                    ),
                                    attribute
                                )
                            );
                    }
                    continue;
                }

                if (attribute.IsNamespaceDeclaration)
                    continue;

                if (local.Contains("."))
                {
                    if (AttachedEvent(element, attribute, resolver) is { } attached)
                    {
                        ReadAttachedEvent(scan, type, attribute, attached, resolver);
                        scan.LoaderAttribute ??= attribute;
                    }
                    continue;
                }

                if (
                    resolver.FindProperty(type, local) is null
                    && resolver.FindEvent(type, local) is not null
                )
                {
                    scan.Events.Add(
                        new EventHook(local, attribute.Value.Trim(), XamlTypeResolver.Fqn(type))
                    );
                    scan.LoaderAttribute ??= attribute;
                }
            }
        }

        scan.LiveResource = XamlEmitter.LiveReferenceIn(root, rootClass, resolver);

        if (scan.Events.Count > 0 && !HasConnectEvent(rootClass))
            scan.Problems.Add(
                At(
                    (IXmlLineInfo?)scan.LoaderAttribute ?? root,
                    $"{rootClass.Name} is not a FrameworkElement, so the loader has no ConnectEvent to wire "
                        + "the handlers its markup names; subscribe them in code"
                )
            );

        return scan;
    }

    static MarkupSite At(IXmlLineInfo at, string message) =>
        at.HasLineInfo()
            ? new MarkupSite(message, at.LineNumber, at.LinePosition)
            : new MarkupSite(message, 0, 0);

    static bool InTemplate(XElement element, XamlTypeResolver resolver)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (resolver.SymbolOf(parent) is { } type && XamlEmitter.PushesNameScope(type))
                return true;
        }

        return false;
    }

    static (INamedTypeSymbol Owner, IEventSymbol Event)? AttachedEvent(
        XElement element,
        XAttribute attribute,
        XamlTypeResolver resolver
    )
    {
        var local = attribute.Name.LocalName;
        var dot = local.IndexOf('.');
        var ns = attribute.Name.NamespaceName;
        if (attribute.IsNamespaceDeclaration || dot <= 0 || ns == XamlTypeResolver.DirectiveNs)
            return null;

        if (ns.Length == 0)
            ns = element.GetDefaultNamespace().NamespaceName is { Length: > 0 } declared
                ? declared
                : XamlTypeResolver.PresentationNs;

        var owner = resolver.Resolve(ns, local.Substring(0, dot));
        var name = local.Substring(dot + 1);
        if (owner is null || resolver.FindAttachedValueType(owner, name) is not null)
            return null;

        return resolver.FindEvent(owner, name) is { } found ? (owner, found) : null;
    }

    // The owner's routed event, not the element's own of that name: MenuItem.Click is not ButtonBase.Click.
    static void ReadAttachedEvent(
        Scan scan,
        INamedTypeSymbol type,
        XAttribute attribute,
        (INamedTypeSymbol Owner, IEventSymbol Event) attached,
        XamlTypeResolver resolver
    )
    {
        var handler = attribute.Value.Trim();
        var name = attached.Event.Name;
        var element = XamlTypeResolver.Fqn(type);

        if (
            RoutedEventOf(attached.Owner, name) is { } routed
            && XamlTypeResolver.DerivesFrom(type, "global::Noesis.UIElement")
        )
            scan.Events.Add(
                new EventHook(
                    name,
                    handler,
                    element,
                    routed,
                    XamlTypeResolver.Fqn(attached.Event.Type)
                )
            );
        else if (
            SymbolEqualityComparer.Default.Equals(resolver.FindEvent(type, name), attached.Event)
        )
            scan.Events.Add(new EventHook(name, handler, element));
        else
        {
            scan.UnresolvedHandlers = true;
            scan.Unwireable.Add(attribute);
        }
    }

    static string? RoutedEventOf(ITypeSymbol owner, string name)
    {
        for (var type = owner; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(name + "Event"))
            {
                var memberType = member switch
                {
                    IPropertySymbol
                    {
                        IsStatic: true,
                        DeclaredAccessibility: Accessibility.Public
                    } p => p.Type,
                    IFieldSymbol
                    {
                        IsStatic: true,
                        DeclaredAccessibility: Accessibility.Public
                    } f => f.Type,
                    _ => null,
                };

                if (
                    memberType is not null
                    && XamlTypeResolver.Fqn(memberType) == "global::Noesis.RoutedEvent"
                )
                    return $"{XamlTypeResolver.Fqn(type)}.{name}Event";
            }
        }

        return null;
    }

    // A value naming no method of the class is a plain value, whatever type it is written on.
    static bool DeclaresMethod(INamedTypeSymbol rootClass, string name)
    {
        for (var type = rootClass; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(name.Trim()).OfType<IMethodSymbol>().Any())
                return true;
        }

        return false;
    }

    static bool IsHandlerShaped(XAttribute attribute) =>
        !attribute.IsNamespaceDeclaration
        && attribute.Name.NamespaceName != XamlTypeResolver.DirectiveNs
        && !attribute.Name.LocalName.Contains(".")
        && attribute.Value.Length > 0
        && attribute.Value.All(c => char.IsLetterOrDigit(c) || c == '_')
        && !char.IsDigit(attribute.Value[0]);

    public static void Emit(
        CodeWriter w,
        INamedTypeSymbol rootClass,
        Scan scan,
        string logicalName,
        bool compiled,
        IReadOnlyList<string> roots
    )
    {
        foreach (var attribute in roots)
            w.Line(attribute);

        using (w.Block("public void InitializeComponent()"))
        {
            if (compiled)
            {
                w.Line("BuildXamlTree();");
            }
            else
            {
                // A reload rebuilds the tree, so a cached element would be the detached one.
                foreach (var name in Distinct(scan.Names))
                    w.Line($"{Field(name.Name)} = null;");

                w.Line(
                    $"global::Noesis.GUI.LoadComponent(this, {XamlEmitter.Quote(logicalName)});"
                );
            }
        }

        foreach (var name in Distinct(scan.Names))
        {
            var member = Escape(name.Name);
            var field = Field(name.Name);
            var hiding = Inherits(rootClass, name.Name) ? "new " : "";

            w.Line();
            w.Line($"private {name.TypeFqn} {field};");
            w.Line($"{name.Modifier} {hiding}{name.TypeFqn} {member}");
            using (w.Indented())
            {
                // Assigned during the build, so a constructor can read it before the first layout pass.
                if (compiled)
                    w.Line($"=> {field};");
                else
                    w.Line($"=> {field} ??= ({name.TypeFqn})this.FindName(\"{name.Name}\");");
            }
        }

        if (compiled || scan.Events.Count == 0 || !HasConnectEvent(rootClass))
            return;

        w.Line();
        using (
            w.Block(
                "protected override bool ConnectEvent(object source, string eventName, string handlerName)"
            )
        )
        {
            var index = 0;
            foreach (var hook in Distinct(scan.Events))
            {
                var source = $"__source{index++}";
                // Matched on the event too: one handler may be bound across several element types.
                using (
                    w.Block(
                        $"if (handlerName == nameof({hook.HandlerName}) "
                            + $"&& eventName == \"{hook.EventName}\" "
                            + $"&& source is {hook.OwnerFqn} {source})"
                    )
                )
                {
                    w.Line(
                        hook.RoutedEvent is { } routed
                            ? $"{source}.AddHandler({routed}, new {hook.HandlerType}({hook.HandlerName}));"
                            : $"{source}.{hook.EventName} += {hook.HandlerName};"
                    );
                    w.Line("return true;");
                }
            }

            w.Line("return base.ConnectEvent(source, eventName, handlerName);");
        }
    }

    static bool HasConnectEvent(INamedTypeSymbol rootClass) =>
        XamlTypeResolver.DerivesFrom(rootClass, "global::Noesis.FrameworkElement");

    // Two underscores: one would collide with an x:Name that starts with one.
    internal static string Field(string name) => "__" + name;

    // The accessor can be no more visible than its type, and x:FieldModifier only narrows it.
    static string Modifier(
        XElement element,
        INamedTypeSymbol type,
        INamedTypeSymbol rootClass,
        Scan scan
    )
    {
        var accessibility = Accessibility.Public;
        if (
            element.Attribute(XName.Get("FieldModifier", XamlTypeResolver.DirectiveNs)) is
            { } written
        )
        {
            if (Stated(written.Value) is { } stated)
                accessibility = stated;
            else
                scan.Problems.Add(
                    At(
                        written,
                        $"x:FieldModifier='{written.Value}' is not an accessibility C# can declare"
                    )
                );
        }

        if (Rank(Effective(type)) < Rank(Effective(rootClass)))
            accessibility = Narrowest(accessibility, Effective(type));

        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "private",
        };
    }

    static Accessibility? Stated(string modifier) =>
        modifier.Trim() switch
        {
            "public" or "Public" => Accessibility.Public,
            "internal" or "Friend" or "NotPublic" => Accessibility.Internal,
            "protected internal" or "internal protected" => Accessibility.ProtectedOrInternal,
            "protected" => Accessibility.Protected,
            "private protected" or "protected private" => Accessibility.ProtectedAndInternal,
            "private" or "Private" => Accessibility.Private,
            _ => null,
        };

    static Accessibility Effective(INamedTypeSymbol type)
    {
        var effective = Accessibility.Public;
        for (var current = type; current is not null; current = current.ContainingType)
            effective = Narrowest(effective, current.DeclaredAccessibility);

        return effective;
    }

    static Accessibility Narrowest(Accessibility a, Accessibility b) =>
        a == b ? a
        : Rank(a) != Rank(b) ? (Rank(a) < Rank(b) ? a : b)
        : Accessibility.ProtectedAndInternal;

    static int Rank(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => 5,
            Accessibility.ProtectedOrInternal => 4,
            Accessibility.Internal or Accessibility.Protected => 3,
            Accessibility.ProtectedAndInternal => 2,
            _ => 1,
        };

    internal static bool Inherits(INamedTypeSymbol rootClass, string name)
    {
        for (var type = rootClass.BaseType; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(name).Any(m => m.DeclaredAccessibility != Accessibility.Private))
                return true;
        }

        return false;
    }

    internal static string Escape(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name)
        == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? name
            : "@" + name;

    static IEnumerable<NamedElement> Distinct(List<NamedElement> names) =>
        names.GroupBy(n => n.Name).Select(g => g.First());

    /// <summary>Names one document cannot carry: two elements sharing an x:Name would emit one field
    /// and two assignments, and a name equal to the class would emit a member that hides its own
    /// type.</summary>
    // With no resolvable handler no ConnectEvent is generated, and the class's own can wire these.
    internal static IEnumerable<MarkupSite> Unwired(Scan scan) =>
        scan.Events.Count == 0
            ? Enumerable.Empty<MarkupSite>()
            : scan.Unwireable.Select(a =>
                At(
                    a,
                    $"<{a.Parent!.Name.LocalName} {a.Name.LocalName}=\"{a.Value}\"> names a handler on a type "
                        + "the compiler cannot resolve, so the generated ConnectEvent cannot wire it"
                )
            );

    // The class's own source, before any generator adds to it.
    internal static IEnumerable<MarkupSite> Collisions(Scan scan, INamedTypeSymbol rootClass)
    {
        var generated = scan.NeedsLoader
            ? new[] { "InitializeComponent" }
            : new[] { "InitializeComponent", "BuildXamlTree" };
        if (scan.NeedsLoader && scan.Events.Count > 0 && HasConnectEvent(rootClass))
            generated = [.. generated, "ConnectEvent"];

        foreach (var member in generated)
        {
            if (!rootClass.GetMembers(member).IsEmpty)
                yield return At(
                    scan.ClassSite,
                    $"{rootClass.Name} declares {member}, which the compiler generates; remove it"
                );
        }

        foreach (var named in Distinct(scan.Names))
        {
            if (
                !rootClass.GetMembers(named.Name).IsEmpty
                || !rootClass.GetMembers(Field(named.Name)).IsEmpty
            )
                yield return At(
                    named.Declared,
                    $"x:Name='{named.Name}' collides with a member {rootClass.Name} declares"
                );
        }
    }

    internal static MarkupSite? NameConflict(Scan scan, INamedTypeSymbol rootClass)
    {
        foreach (var group in scan.Names.GroupBy(n => n.Name))
        {
            var at = group.ElementAt(group.Count() > 1 ? 1 : 0).Declared;

            if (group.Select(n => n.TypeFqn).Distinct(StringComparer.Ordinal).Count() > 1)
                return At(at, $"x:Name='{group.Key}' is used by elements of different types");

            if (group.Count() > 1)
                return At(at, $"x:Name='{group.Key}' is used more than once");

            if (group.Key == rootClass.Name)
                return At(at, $"x:Name='{group.Key}' is the name of its own class");

            if (scan.Names.Any(n => Field(n.Name) == group.Key))
                return At(at, $"x:Name='{group.Key}' is the field generated for another x:Name");
        }

        return null;
    }

    static IEnumerable<EventHook> Distinct(List<EventHook> events) => events.Distinct();
}
