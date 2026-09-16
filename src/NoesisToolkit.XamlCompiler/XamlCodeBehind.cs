using System;
using System.Collections.Generic;
using System.Linq;
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
    internal readonly record struct NamedElement(string Name, string TypeFqn);

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

        public bool NeedsLoader => Events.Count > 0 || UnresolvedHandlers;
    }

    /// <summary>
    /// Types every <c>x:Name</c> against the compilation rather than its XML tag: <c>&lt;MenuItem&gt;</c>
    /// is <c>Noesis.MenuItem</c>, which the tag name alone would resolve in the code-behind's namespace.
    /// </summary>
    public static Scan Read(XElement root, XamlTypeResolver resolver)
    {
        var scan = new Scan();

        foreach (var element in root.DescendantsAndSelf())
        {
            var type = resolver.SymbolOf(element);

            if (type is null)
            {
                // The loader binds what the compiler cannot, so a suspected handler is enough.
                if (
                    element
                        .Attributes()
                        .Any(a =>
                            IsHandlerShaped(a) || AttachedEvent(element, a, resolver) is not null
                        )
                )
                    scan.UnresolvedHandlers = true;
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
                        scan.Names.Add(
                            new NamedElement(attribute.Value, XamlTypeResolver.Fqn(type))
                        );
                    continue;
                }

                if (attribute.IsNamespaceDeclaration)
                    continue;

                if (local.Contains("."))
                {
                    if (AttachedEvent(element, attribute, resolver) is { } attached)
                        ReadAttachedEvent(scan, type, attribute.Value.Trim(), attached, resolver);
                    continue;
                }

                if (
                    resolver.FindProperty(type, local) is null
                    && resolver.FindEvent(type, local) is not null
                )
                    scan.Events.Add(
                        new EventHook(local, attribute.Value.Trim(), XamlTypeResolver.Fqn(type))
                    );
            }
        }

        return scan;
    }

    static bool InTemplate(XElement element, XamlTypeResolver resolver)
    {
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            var type = resolver.SymbolOf(parent);
            if (
                type is not null
                && XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkTemplate")
            )
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
        string handler,
        (INamedTypeSymbol Owner, IEventSymbol Event) attached,
        XamlTypeResolver resolver
    )
    {
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
            scan.UnresolvedHandlers = true;
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
        bool compiled
    )
    {
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
                    w.Line($"_{name.Name} = null;");

                w.Line($"global::Noesis.GUI.LoadComponent(this, \"{logicalName}\");");
            }
        }

        foreach (var name in Distinct(scan.Names))
        {
            var member = Escape(name.Name);
            var field = "_" + name.Name;
            var hiding = Inherits(rootClass, name.Name) ? "new " : "";

            w.Line();
            w.Line($"private {name.TypeFqn} {field};");
            w.Line($"public {hiding}{name.TypeFqn} {member}");
            using (w.Indented())
            {
                // Assigned during the build, so a constructor can read it before the first layout pass.
                if (compiled)
                    w.Line($"=> {field};");
                else
                    w.Line($"=> {field} ??= ({name.TypeFqn})this.FindName(\"{name.Name}\");");
            }
        }

        if (compiled || scan.Events.Count == 0)
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

    static bool Inherits(INamedTypeSymbol rootClass, string name)
    {
        for (var type = rootClass.BaseType; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(name).Length > 0)
                return true;
        }

        return false;
    }

    static string Escape(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name)
        == Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? name
            : "@" + name;

    static IEnumerable<NamedElement> Distinct(List<NamedElement> names) =>
        names.GroupBy(n => n.Name).Select(g => g.First());

    /// <summary>Names one document cannot carry: two elements sharing an x:Name would emit one field
    /// and two assignments, and a name equal to the class would emit a member that hides its own
    /// type.</summary>
    internal static string? NameConflict(Scan scan, INamedTypeSymbol rootClass)
    {
        foreach (var group in scan.Names.GroupBy(n => n.Name))
        {
            if (group.Select(n => n.TypeFqn).Distinct(StringComparer.Ordinal).Count() > 1)
                return $"x:Name='{group.Key}' is used by elements of different types";

            if (group.Count() > 1)
                return $"x:Name='{group.Key}' is used more than once";

            if (group.Key == rootClass.Name)
                return $"x:Name='{group.Key}' is the name of its own class";
        }

        return null;
    }

    static IEnumerable<EventHook> Distinct(List<EventHook> events) => events.Distinct();
}
