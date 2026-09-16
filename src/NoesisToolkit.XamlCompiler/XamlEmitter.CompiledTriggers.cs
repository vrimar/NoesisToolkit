using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Xaml;

// A compiled trigger writes at local precedence while a native one writes below it, so a compiled
// setter may not touch a property any remaining native trigger also sets.
sealed partial class XamlEmitter
{
    const string TriggerSetFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledTriggerSet";

    const string TriggerSpecFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledTriggerSpec";

    const string TriggerConditionFqn =
        "global::NoesisToolkit.Mvvm.CodeGen.CompiledTriggerCondition";

    const string CompiledSetterFqn = "global::NoesisToolkit.Mvvm.CodeGen.CompiledSetter";

    readonly HashSet<XElement> _suppressed = new HashSet<XElement>();

    void PlanCompiledTriggers(XElement owner, string target, INamedTypeSymbol type, XElement style)
    {
        if (!OptedIntoCompiledBindings(owner))
            return;

        if (!XamlTypeResolver.DerivesFrom(type, "global::Noesis.FrameworkElement"))
            return;

        var wrapper = style.Elements().FirstOrDefault(e => e.Name.LocalName == "Style.Triggers");
        if (wrapper is null)
            return;

        var specs = PlanTriggers(owner, type, wrapper, templateScoped: false);
        if (specs is null)
            return;

        EmitCompiledBind(
            target,
            receiver => $"{TriggerSetFqn}.Bind({receiver}, new {TriggerSpecFqn}[] {{ {specs} }})"
        );
    }

    // Wires before content: the trigger set has to join the content root's one wiring slot, and
    // that slot is emitted the moment the root has taken its last child.
    void PlanTemplateTriggers(XElement template)
    {
        if (!OptedIntoCompiledBindings(template))
            return;

        var wrapper = template
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == template.Name.LocalName + ".Triggers");
        if (wrapper is null)
            return;

        var content = template.Elements().FirstOrDefault(e => e.Name.LocalName.IndexOf('.') < 0);
        if (content is null)
            return;

        var contentType = resolver.SymbolOf(content);
        if (
            contentType is null
            || !XamlTypeResolver.DerivesFrom(contentType, "global::Noesis.FrameworkElement")
        )
            return;

        var specs = PlanTriggers(content, contentType, wrapper, templateScoped: true);
        if (specs is null)
            return;

        // Inside the wire this would allocate per clone, and a re-bind could never recognise it.
        var shared = NextName("triggers");
        _lines.Add($"var {shared} = new {TriggerSpecFqn}[] {{ {specs} }};");
        AddPendingWire(content, $"{TriggerSetFqn}.Bind(__e, {shared});");
    }

    string? PlanTriggers(
        XElement owner,
        INamedTypeSymbol type,
        XElement wrapper,
        bool templateScoped
    )
    {
        var triggers = wrapper.Elements().ToList();
        var compiled = new List<(XElement Element, string Spec, HashSet<string> Properties)>();
        var refused = new List<string>();

        using (Speculate())
        {
            foreach (var trigger in triggers)
            {
                BeginAttempt();
                if (CompileTrigger(owner, type, trigger, templateScoped) is { } plan)
                    compiled.Add((trigger, plan.Spec, plan.Properties));
                else
                    refused.Add(_refusal ?? "trigger-unclassified");
            }
        }

        var claimed = compiled.Count;
        var demoted = true;
        while (demoted && compiled.Count > 0)
        {
            demoted = false;
            var contested = new HashSet<string>(StringComparer.Ordinal);
            var opaque = false;
            foreach (var trigger in triggers)
            {
                if (compiled.Any(c => ReferenceEquals(c.Element, trigger)))
                    continue;

                if (NativeSetterProperties(trigger) is not { } names)
                {
                    opaque = true;
                    break;
                }

                foreach (var name in names)
                    contested.Add(name);
            }

            if (opaque)
            {
                compiled.Clear();
                break;
            }

            for (var i = compiled.Count - 1; i >= 0; i--)
            {
                if (compiled[i].Properties.Overlaps(contested))
                {
                    compiled.RemoveAt(i);
                    demoted = true;
                }
            }
        }

        // Outside the speculative scope, which would roll these back.
        foreach (var reason in refused)
            Tally.TriggerFell(reason);

        for (var i = compiled.Count; i < claimed; i++)
            Tally.TriggerFell("trigger-property-contested");

        Tally.TriggersCompiled += compiled.Count;

        if (compiled.Count == 0)
            return null;

        foreach (var plan in compiled)
            _suppressed.Add(plan.Element);

        return string.Join(", ", compiled.Select(c => c.Spec).ToArray());
    }

    (string Spec, HashSet<string> Properties)? CompileTrigger(
        XElement owner,
        INamedTypeSymbol type,
        XElement trigger,
        bool templateScoped
    )
    {
        var kind = trigger.Name.LocalName;
        if (kind is not ("DataTrigger" or "MultiDataTrigger" or "Trigger" or "MultiTrigger"))
        {
            Note("trigger-kind-unsupported");
            return null;
        }

        var driven = kind is "Trigger" or "MultiTrigger";
        var multi = kind is "MultiTrigger" or "MultiDataTrigger";
        var conditions = new List<string>();
        var setterElements = new List<XElement>();

        foreach (var attribute in trigger.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            if (kind == "DataTrigger" && attribute.Name.LocalName is "Binding" or "Value")
                continue;

            if (kind == "Trigger" && attribute.Name.LocalName is "Property" or "Value")
                continue;

            Note("trigger-attribute-unsupported");
            return null;
        }

        foreach (var child in trigger.Elements())
        {
            var local = child.Name.LocalName;
            if (local == kind + ".Setters" || local == "Setter")
            {
                foreach (var setter in local == "Setter" ? new[] { child } : child.Elements())
                {
                    if (setter.Name.LocalName != "Setter")
                    {
                        Note("trigger-setter-unsupported");
                        return null;
                    }

                    setterElements.Add(setter);
                }

                continue;
            }

            if (local == kind + ".Conditions" && multi)
                continue;

            if (local == "DataTrigger.Binding" && kind == "DataTrigger")
                continue;

            Note(
                local.EndsWith("EnterActions", StringComparison.Ordinal)
                || local.EndsWith("ExitActions", StringComparison.Ordinal)
                    ? "trigger-has-actions"
                    : "trigger-child-unsupported"
            );
            return null;
        }

        if (!multi)
        {
            var condition = driven
                ? PropertyCondition(
                    owner,
                    type,
                    templateScoped,
                    trigger.Attribute("Property")?.Value,
                    trigger.Attribute("Value")?.Value
                )
                : TriggerCondition(
                    owner,
                    templateScoped,
                    trigger.Attribute("Binding")?.Value,
                    trigger
                        .Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "DataTrigger.Binding"),
                    trigger.Attribute("Value")?.Value
                );
            if (condition is null)
                return null;

            conditions.Add(condition);
        }
        else
        {
            var wrapper = trigger
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName == kind + ".Conditions");
            if (wrapper is null)
            {
                Note("trigger-conditions-missing");
                return null;
            }

            var allowed = driven ? "Property" : "Binding";
            foreach (var child in wrapper.Elements())
            {
                if (child.Name.LocalName != "Condition")
                {
                    Note("trigger-condition-unsupported");
                    return null;
                }

                foreach (var attribute in child.Attributes())
                {
                    if (
                        !attribute.IsNamespaceDeclaration
                        && attribute.Name.LocalName != allowed
                        && attribute.Name.LocalName != "Value"
                    )
                    {
                        Note("trigger-condition-attribute-unsupported");
                        return null;
                    }
                }

                var condition = driven
                    ? PropertyCondition(
                        owner,
                        type,
                        templateScoped,
                        child.Attribute("Property")?.Value,
                        child.Attribute("Value")?.Value
                    )
                    : TriggerCondition(
                        owner,
                        templateScoped,
                        child.Attribute("Binding")?.Value,
                        null,
                        child.Attribute("Value")?.Value
                    );
                if (condition is null)
                    return null;

                conditions.Add(condition);
            }

            if (conditions.Count == 0)
            {
                Note("trigger-conditions-missing");
                return null;
            }
        }

        if (setterElements.Count == 0)
        {
            Note("trigger-has-no-setters");
            return null;
        }

        var setters = new List<string>();
        var properties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setter in setterElements)
        {
            if (CompileTriggerSetter(owner, type, setter, templateScoped) is not { } plan)
                return null;

            setters.Add(plan.Spec);
            properties.Add(plan.Key);
        }

        var scoped = templateScoped ? "TemplateScoped = true, " : "";
        if (driven)
            scoped += "PropertyDriven = true, ";

        var spec =
            $"new {TriggerSpecFqn} {{ {scoped}"
            + $"Conditions = new {TriggerConditionFqn}[] {{ {string.Join(", ", conditions.ToArray())} }}, "
            + $"Setters = new {CompiledSetterFqn}[] {{ {string.Join(", ", setters.ToArray())} }} }}";
        return (spec, properties);
    }

    // In a template the property is the templated parent's, not the content root's.
    string? PropertyCondition(
        XElement owner,
        INamedTypeSymbol type,
        bool templateScoped,
        string? propertyName,
        string? rawValue
    )
    {
        if (propertyName is null || rawValue is null)
        {
            Note("trigger-condition-has-no-value");
            return null;
        }

        var source = templateScoped ? TemplatedSource(owner) : new BindingSource(null, owner, type);
        if (source is not { } from)
        {
            Note("trigger-source-unresolved");
            return null;
        }

        if (ElementPath(from, propertyName.Trim()) is not { } resolved)
            return null;

        var constant = TriggerConstant(owner, rawValue, resolved.Type);
        if (constant is null)
        {
            Note("trigger-value-unsupported");
            return null;
        }

        return $"new {TriggerConditionFqn} {{ "
            + $"Part = {PartExpression(from, resolved)}, "
            + $"Value = {constant} }}";
    }

    string? TriggerCondition(
        XElement owner,
        bool templateScoped,
        string? bindingRaw,
        XElement? bindingElement,
        string? rawValue
    )
    {
        if (rawValue is null)
        {
            Note("trigger-condition-has-no-value");
            return null;
        }

        MarkupCall? call = null;
        if (bindingRaw is not null)
        {
            if (!XamlMarkupParser.IsMarkup(bindingRaw))
            {
                Note("trigger-condition-not-a-binding");
                return null;
            }

            call = XamlMarkupParser.Parse(bindingRaw);
        }
        else if (bindingElement is not null)
        {
            var inner = bindingElement.Elements().FirstOrDefault();
            if (
                inner is null
                || bindingElement.Elements().Skip(1).Any()
                || inner.Name.LocalName != "Binding"
            )
            {
                Note("trigger-condition-not-a-binding");
                return null;
            }

            call = BindingElementCall(inner);
        }

        if (call is null || call.Name != "Binding")
        {
            Note("trigger-condition-not-a-binding");
            return null;
        }

        // A converter or format would change the value the constant is compared against.
        if (
            call.Named.Any(p =>
                p.Key
                    is "Converter"
                        or "ConverterParameter"
                        or "StringFormat"
                        or "Mode"
                        or "UpdateSourceTrigger"
            )
        )
        {
            Note("trigger-condition-binding-knob");
            return null;
        }

        if (PlainPath(call) is not { } path)
        {
            Note("trigger-condition-path-unsupported");
            return null;
        }

        // Evaluated on the templated parent, where TemplatedParent names whatever that one is in.
        if (templateScoped && RelativeMode(call) == "TemplatedParent")
        {
            Note("trigger-condition-past-the-templated-parent");
            return null;
        }

        // A condition is not written on an element, so its Self is the element the template is
        // applied to rather than the root the trigger set is anchored on.
        var from =
            templateScoped && SelfRelative(call) ? TemplatedSource(owner)
            : templateScoped && AncestorRelative(call) ? AncestorOfTemplatedParent(owner, call)
            : ResolveSource(owner, call);
        if (from is not { } source)
            return null;

        // Each reads the templated parent's DataContext, not the one the root rebinds for itself.
        if (
            templateScoped
            && RebindsDataContext(owner)
            && (
                source.Resolver is null
                || (SelfRelative(call) && TrySplitDataContextHop(path, out _))
            )
        )
        {
            Note("trigger-condition-context-rebound");
            return null;
        }

        if (ResolvePath(source, path) is not { } resolved)
            return null;

        // Native converts the constant to whatever an object path holds at run time.
        if (resolved.Type.SpecialType == SpecialType.System_Object && !IsNullMarkup(rawValue))
        {
            Note("trigger-condition-path-is-object");
            return null;
        }

        var constant = TriggerConstant(owner, rawValue, resolved.Type);
        if (constant is null)
        {
            Note("trigger-value-unsupported");
            return null;
        }

        return $"new {TriggerConditionFqn} {{ "
            + $"Part = {PartExpression(source, resolved)}, "
            + $"Value = {constant} }}";
    }

    static bool SelfRelative(MarkupCall call) =>
        NamedValue(call, "RelativeSource") is MarkupCall relative
        && RelativeSourceParts(relative).Mode == "Self";

    static string? RelativeMode(MarkupCall call) =>
        NamedValue(call, "RelativeSource") is MarkupCall relative
            ? RelativeSourceParts(relative).Mode
            : null;

    static bool AncestorRelative(MarkupCall call) =>
        NamedValue(call, "RelativeSource") is MarkupCall relative
        && RelativeSourceParts(relative).AncestorType is not null;

    // The walk starts above the templated parent, so nothing in the document says what it reaches.
    BindingSource? AncestorOfTemplatedParent(XElement owner, MarkupCall call)
    {
        if (ResolveSource(owner, call) is null)
            return null;

        var relative = (MarkupCall)NamedValue(call, "RelativeSource")!;
        if (ResolveTypeSymbol(owner, RelativeSourceParts(relative).AncestorType!) is not { } type)
            return null;

        return new BindingSource(
            $"__s => {CompiledBindingFqn}.TemplatedParent(__s) is {{ }} __p"
                + $" ? {CompiledBindingFqn}.FindAncestor(__p, typeof({XamlTypeResolver.Fqn(type)}))"
                + " : null",
            owner,
            type,
            detached: true
        );
    }

    static bool RebindsDataContext(XElement element) =>
        element.Attribute("DataContext") is not null
        || element
            .Elements()
            .Any(e => e.Name.LocalName.EndsWith(".DataContext", StringComparison.Ordinal));

    static bool IsNullMarkup(string raw) =>
        XamlMarkupParser.IsMarkup(raw)
        && XamlMarkupParser.Parse(raw) is { Name: "x:Null" or "Null" };

    (string Spec, string Key)? CompileTriggerSetter(
        XElement owner,
        INamedTypeSymbol type,
        XElement setter,
        bool templateScoped
    )
    {
        if (setter.HasElements)
        {
            Note("trigger-setter-has-elements");
            return null;
        }

        foreach (var attribute in setter.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            if (attribute.Name.LocalName is "Property" or "Value")
                continue;

            if (attribute.Name.LocalName == "TargetName" && templateScoped)
                continue;

            Note("trigger-setter-attribute-unsupported");
            return null;
        }

        var propertyName = setter.Attribute("Property")?.Value.Trim();
        var rawValue = setter.Attribute("Value")?.Value;
        if (propertyName is null || rawValue is null || propertyName.IndexOf('.') >= 0)
        {
            Note("trigger-setter-property-unsupported");
            return null;
        }

        var targetName = setter.Attribute("TargetName")?.Value.Trim();
        var targetType = type;
        if (templateScoped)
        {
            // A template trigger setter without a name would write the templated parent.
            if (targetName is null)
            {
                Note("trigger-target-unnamed");
                return null;
            }

            if (NamedInTemplate(owner, targetName) is not { } named)
            {
                Note("trigger-target-not-found");
                return null;
            }

            targetType = named;
        }

        if (resolver.FindProperty(targetType, propertyName) is not { } property)
        {
            Note("trigger-setter-property-unsupported");
            return null;
        }

        if (PlainSlot(property) is not { } slot)
            return null;

        // A template's own values rank below its triggers: a named target is never outranked.
        if (!templateScoped && StyleSetterOutranked(owner, type, propertyName))
        {
            Note("trigger-setter-outranked");
            return null;
        }

        var value = TriggerConstant(owner, rawValue, slot.Type);
        if (value is null)
        {
            Note("trigger-setter-value-unsupported");
            return null;
        }

        var fields = $"Property = {slot.Reference}, Value = {value}";
        if (slot.Assign is not null)
            fields += $", Assign = {slot.Assign}";
        if (targetName is not null)
            fields += $", TargetName = {Quote(targetName)}";

        return ($"new {CompiledSetterFqn} {{ {fields} }}", SetterKey(targetName, propertyName));
    }

    // Each of these ranks above a native style trigger, which a compiled local write would beat.
    bool StyleSetterOutranked(XElement owner, INamedTypeSymbol type, string property)
    {
        if (
            owner
                .Attributes()
                .Any(a =>
                    !a.IsNamespaceDeclaration && SetterPropertyName(a.Name.LocalName) == property
                )
        )
            return true;

        if (
            owner
                .Elements()
                .Any(e => e.Name.LocalName.EndsWith("." + property, StringComparison.Ordinal))
        )
            return true;

        var implicitContent =
            owner.Elements().Any(e => e.Name.LocalName.IndexOf('.') < 0)
            || owner.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value));
        if (implicitContent && ContentPropertyOf(type) == property)
            return true;

        return NamedByTemplateTrigger(owner, property);
    }

    bool NamedByTemplateTrigger(XElement element, string property)
    {
        var name = (
            element.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs))
            ?? element.Attribute("Name")
        )?.Value.Trim();
        if (name is null || TemplateOwner(element) is not { } template)
            return false;

        foreach (
            var wrapper in template
                .Elements()
                .Where(e => e.Name.LocalName.EndsWith(".Triggers", StringComparison.Ordinal))
        )
        {
            foreach (var setter in wrapper.Elements().SelectMany(TriggerSetters))
            {
                if (setter.Attribute("TargetName")?.Value.Trim() != name)
                    continue;

                // Unreadable counts as touching everything.
                if (
                    setter.Attribute("Property")?.Value is not { } written
                    || SetterPropertyName(written) == property
                )
                    return true;
            }
        }

        return false;
    }

    static IEnumerable<XElement> TriggerSetters(XElement trigger) =>
        trigger
            .Elements()
            .SelectMany(child =>
                child.Name.LocalName == "Setter" ? new[] { child }
                : Array.IndexOf(SetterWrappers, child.Name.LocalName) >= 0
                    ? child.Elements().Where(e => e.Name.LocalName == "Setter")
                : Enumerable.Empty<XElement>()
            );

    // Compared by name alone: an AddOwner alias is the same property under another owner.
    static string SetterPropertyName(string written)
    {
        var name = written.Trim().Trim('(', ')');
        return name.Substring(name.LastIndexOf('.') + 1);
    }

    INamedTypeSymbol? NamedInTemplate(XElement nameRoot, string name)
    {
        foreach (var element in NameScopeOf(nameRoot))
        {
            if (
                element.Attribute(XName.Get("Name", XamlTypeResolver.DirectiveNs))?.Value.Trim()
                == name
            )
                return resolver.SymbolOf(element);
        }

        return null;
    }

    static string SetterKey(string? targetName, string property) =>
        (targetName ?? "") + "|" + property;

    // Both resource forms emit an extension object that SetValue stores rather than resolves.
    static readonly string[] UnresolvedMarkup =
    {
        "StaticResource",
        "DynamicResource",
        "Binding",
        "TemplateBinding",
    };

    string? TriggerConstant(XElement owner, string raw, ITypeSymbol type)
    {
        if (!XamlMarkupParser.IsMarkup(raw))
            return ConvertValue(owner, XamlMarkupParser.Unescape(raw), type);

        var call = XamlMarkupParser.Parse(raw);
        if (call is null || Array.IndexOf(UnresolvedMarkup, call.Name) >= 0)
        {
            Note("trigger-value-is-a-lookup");
            return null;
        }

        return MarkupValue(owner, call, type);
    }

    static readonly string[] SetterWrappers =
    {
        "Trigger.Setters",
        "DataTrigger.Setters",
        "MultiDataTrigger.Setters",
        "MultiTrigger.Setters",
    };

    // Null: not all setters could be named, which has to count as touching everything.
    static HashSet<string>? NativeSetterProperties(XElement trigger)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in trigger.Elements())
        {
            var setters =
                child.Name.LocalName == "Setter" ? new[] { child }
                : Array.IndexOf(SetterWrappers, child.Name.LocalName) >= 0
                    ? child.Elements().ToArray()
                : null;

            if (setters is null)
                continue;

            foreach (var setter in setters)
            {
                if (setter.Name.LocalName != "Setter")
                    return null;

                if (setter.Attribute("Property")?.Value.Trim() is not { } name)
                    return null;

                names.Add(
                    SetterKey(
                        setter.Attribute("TargetName")?.Value.Trim(),
                        SetterPropertyName(name)
                    )
                );
            }
        }

        return names;
    }
}
