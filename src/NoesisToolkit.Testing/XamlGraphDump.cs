using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Noesis;
using NoesisToolkit.CodeGen;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Testing;

// A template prototype exposes no children, so each template is realized on a host first.
static class XamlGraphDump
{
    const int Frames = 6;

    internal const int Deepest = 30;

    const string Truncation = "<deeper than the walk>";

    internal static bool Truncated(string dump) =>
        dump.Split('\n').Any(line => line.Trim() == Truncation);

    internal static (string Parsed, string Compiled) Dump(
        ResourceDictionary parsed,
        ResourceDictionary compiled,
        bool expandMerged
    )
    {
        var named = new Dictionary<nint, string>();
        if (!expandMerged)
            Pair(parsed, compiled, named);

        return (Dump(parsed, named), Dump(compiled, named));
    }

    internal static (string Parsed, string Compiled) Dump(
        FrameworkElement parsed,
        FrameworkElement compiled
    ) => (DumpRoot(parsed), DumpRoot(compiled));

    // A document merged in is compared on its own, so a matching pair of it is named, not walked.
    static void Pair(
        ResourceDictionary parsed,
        ResourceDictionary compiled,
        Dictionary<nint, string> named
    )
    {
        var left = parsed.MergedDictionaries;
        var right = compiled.MergedDictionaries;
        for (var i = 0; i < left.Count && i < right.Count; i++)
        {
            if (left[i].Source is { } source && SameShape(left[i], right[i]))
                named[Handle(left[i])] = named[Handle(right[i])] = source.OriginalString;
            else
                Pair(left[i], right[i], named);
        }
    }

    static bool SameShape(ResourceDictionary a, ResourceDictionary b)
    {
        if (
            a.MergedDictionaries.Count != b.MergedDictionaries.Count
            || !KeysOf(a).SequenceEqual(KeysOf(b), StringComparer.Ordinal)
        )
            return false;

        for (var i = 0; i < a.MergedDictionaries.Count; i++)
        {
            if (!SameShape(a.MergedDictionaries[i], b.MergedDictionaries[i]))
                return false;
        }

        return true;
    }

    static IEnumerable<string> KeysOf(ResourceDictionary dictionary) =>
        dictionary
            .Keys.Cast<object?>()
            .Select(k => k?.ToString() ?? "")
            .OrderBy(k => k, StringComparer.Ordinal);

    static nint Handle(BaseComponent component) => BaseComponent.getCPtr(component).Handle;

    static readonly Dictionary<nint, string> Unnamed = new Dictionary<nint, string>();

    static string Dump(ResourceDictionary dictionary, Dictionary<nint, string> named)
    {
        var sb = new StringBuilder();
        DumpEntries(dictionary, named, sb, 0);
        return sb.ToString();
    }

    static void DumpEntries(
        ResourceDictionary dictionary,
        Dictionary<nint, string> named,
        StringBuilder sb,
        int depth
    )
    {
        var entries = new List<(string Text, object Key, ResourceDictionary Owner)>();
        var merged = new List<string>();
        Flatten(dictionary, named, entries, merged);

        var pad = new string(' ', depth * 2);
        foreach (var source in merged)
            sb.AppendLine($"{pad}MERGED {source}");

        foreach (var (text, key, owner) in entries.OrderBy(e => e.Text, StringComparer.Ordinal))
        {
            sb.AppendLine($"{pad}KEY {text}");
            DumpValue(owner[key], sb, depth + 1);
        }
    }

    // A key routed through a merged dictionary resolves the same way, so the rest is flattened.
    static void Flatten(
        ResourceDictionary dictionary,
        Dictionary<nint, string> named,
        List<(string Text, object Key, ResourceDictionary Owner)> into,
        List<string> merged
    )
    {
        foreach (var key in dictionary.Keys)
            into.Add((key?.ToString() ?? "", key!, dictionary));

        foreach (var inner in dictionary.MergedDictionaries)
        {
            if (named.TryGetValue(Handle(inner), out var source))
                merged.Add(source);
            else
                Flatten(inner, named, into, merged);
        }
    }

    static string DumpRoot(FrameworkElement root)
    {
        var sb = new StringBuilder();
        sb.AppendLine(root.GetType().Name);
        DumpObject(root, sb, 1, new HashSet<nint>());

        root.Width = 800;
        root.Height = 600;
        Realize(root);

        WalkVisual(root, sb, 1);
        return sb.ToString();
    }

    static void DumpValue(object? value, StringBuilder sb, int depth)
    {
        var pad = new string(' ', depth * 2);

        switch (value)
        {
            case null:
                sb.AppendLine(pad + "null");
                return;
            case DataTemplate template:
                sb.AppendLine(pad + "DataTemplate");
                DumpTemplate(template, sb, depth + 1);
                return;
            case ControlTemplate control:
                sb.AppendLine(
                    pad + $"ControlTemplate TargetType={control.TargetType?.Name ?? "-"}"
                );
                DumpTemplate(control, control.TargetType, sb, depth + 1);
                return;
            case Style style:
                if (style.CanSeal && !style.IsSealed)
                    style.Seal();
                sb.AppendLine(
                    pad
                        + $"Style TargetType={style.TargetType?.Name} BasedOn={(style.BasedOn is null ? "-" : "yes")} "
                        + $"Setters={style.Setters.Count}"
                );
                foreach (var setter in style.Setters)
                {
                    sb.AppendLine(pad + "  " + DescribeSetter(setter));
                    if (setter is Setter { Value: { } held })
                        DumpHeld(held, style.TargetType, sb, depth + 2);
                }
                DumpTriggers(style.Triggers, null, sb, depth + 1);
                if (style.BasedOn is { } basedOn)
                {
                    sb.AppendLine(pad + "  BasedOn");
                    DumpValue(basedOn, sb, depth + 2);
                }
                return;
            case DependencyObject node:
                sb.AppendLine(pad + node.GetType().Name);
                DumpObject(node, sb, depth + 1, new HashSet<nint>());
                return;
            default:
                sb.AppendLine(pad + $"{value.GetType().Name} = {value}");
                return;
        }
    }

    static void DumpTemplate(DataTemplate template, StringBuilder sb, int depth)
    {
        var host = RealizeTemplate(
            new ContentControl(),
            h => ((ContentControl)h).ContentTemplate = template
        );
        DumpTriggers(template.Triggers, CompiledSetsUnder(PresentedRoot(host)), sb, depth);
        WalkVisual(host, sb, depth);
    }

    // A template applies only to a host of its TargetType; anywhere else it is ignored.
    static void DumpTemplate(ControlTemplate control, Type? targetType, StringBuilder sb, int depth)
    {
        var host = RealizeTemplate(HostFor(targetType), h => h.Template = control);
        DumpTriggers(control.Triggers, CompiledSetsUnder(host), sb, depth);
        WalkVisual(host, sb, depth);
    }

    static void DumpHeld(object value, Type? targetType, StringBuilder sb, int depth)
    {
        switch (value)
        {
            case DataTemplate template:
                DumpTemplate(template, sb, depth);
                return;
            case ControlTemplate control:
                DumpTemplate(control, control.TargetType ?? targetType, sb, depth);
                return;
            case Style style:
                DumpValue(style, sb, depth);
                return;
            case DependencyObject node and not Visual:
                DumpObject(node, sb, depth, new HashSet<nint>());
                return;
        }
    }

    static void AppendHeld(StringBuilder block, object? value)
    {
        if (value is null)
            return;

        var held = new StringBuilder();
        DumpHeld(value, null, held, 2);
        AppendLines(block, held);
    }

    static void AppendLines(StringBuilder block, StringBuilder lines)
    {
        foreach (
            var line in lines
                .ToString()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        )
            block.Append('\n').Append(line);
    }

    static void AppendActions(StringBuilder block, string label, TriggerActionCollection? actions)
    {
        if (actions is null || actions.Count == 0)
            return;

        block.Append('\n').Append("  " + label);
        foreach (var action in actions)
        {
            block.Append('\n').Append($"    - {Describe(action)}");
            var held = new StringBuilder();
            DumpObject(action, held, 3, new HashSet<nint>());
            AppendLines(block, held);
        }
    }

    static string DescribeSetter(SetterBase setterBase) =>
        setterBase is not Setter setter
            ? setterBase.GetType().Name
            : $"Setter {PropertyName(setter.Property)} target={setter.TargetName ?? "-"} value={Describe(setter.Value)}";

    // A compiled trigger lives on the realized elements, so both sides meet in one sorted block list.
    static void DumpTriggers(
        TriggerCollection? triggers,
        List<CompiledTriggerSet>? compiled,
        StringBuilder sb,
        int depth
    )
    {
        var blocks = new List<string>();

        if (triggers is not null)
        {
            foreach (var trigger in triggers)
            {
                var block = new StringBuilder(DescribeTrigger(trigger));
                var setters = trigger switch
                {
                    Trigger simple => simple.Setters,
                    DataTrigger data => data.Setters,
                    MultiTrigger multi => multi.Setters,
                    MultiDataTrigger multi => multi.Setters,
                    _ => null,
                };

                if (setters is not null)
                    foreach (var setter in setters.OfType<Setter>())
                    {
                        block
                            .Append('\n')
                            .Append(
                                $"  Setter {PropertyName(setter.Property)} target={setter.TargetName ?? "-"} "
                                    + $"value={Plain(setter.Value)}"
                            );
                        AppendHeld(block, setter.Value);
                    }

                AppendActions(block, "EnterActions", trigger.EnterActions);
                AppendActions(block, "ExitActions", trigger.ExitActions);
                if (trigger is EventTrigger events)
                    AppendActions(block, "Actions", events.Actions);

                blocks.Add(block.ToString());
            }
        }

        // A style's own compiled triggers stay unlisted, as the parsed side only summarizes a style.
        if (compiled is not null)
        {
            foreach (var set in compiled)
            foreach (var spec in set.Specs)
                if (spec.TemplateScoped)
                    blocks.Add(DescribeCompiledTrigger(spec));
        }

        blocks.Sort(StringComparer.Ordinal);
        var pad = new string(' ', depth * 2);
        foreach (var block in blocks)
        foreach (var line in block.Split('\n'))
            sb.AppendLine(pad + line);
    }

    static string DescribeTrigger(TriggerBase trigger) =>
        trigger switch
        {
            Trigger t =>
                $"Trigger {PropertyName(t.Property)} source={SourceName(t.SourceName)} value={Plain(t.Value)}",
            DataTrigger d => $"DataTrigger {Condition(d.Binding)} value={Plain(d.Value)}",
            MultiTrigger { Conditions.Count: 1 } single =>
                $"Trigger {PropertyCondition(single.Conditions[0])}",
            MultiTrigger m => "MultiTrigger ["
                + string.Join(", ", m.Conditions.Select(PropertyCondition))
                + "]",
            // One condition is a DataTrigger however it was spelled; both sides must agree.
            MultiDataTrigger { Conditions.Count: 1 } single => "DataTrigger "
                + $"{Condition(single.Conditions[0].Binding)} "
                + $"value={Plain(single.Conditions[0].Value)}",
            MultiDataTrigger m => "MultiDataTrigger ["
                + string.Join(
                    ", ",
                    m.Conditions.Select(c => $"{Condition(c.Binding)} value={Plain(c.Value)}")
                )
                + "]",
            EventTrigger e =>
                $"EventTrigger {EventName(e.RoutedEvent)} source={SourceName(e.SourceName)}",
            _ => trigger.GetType().Name,
        };

    static string PropertyCondition(Noesis.Condition condition) =>
        $"{PropertyName(condition.Property)} source={SourceName(condition.SourceName)} "
        + $"value={Plain(condition.Value)}";

    static string SourceName(string? name) => string.IsNullOrEmpty(name) ? "-" : name!;

    static string EventName(RoutedEvent? routed) =>
        routed is null ? "-" : $"{OwnerName(routed.OwnerType)}.{routed.Name}";

    // A compiled condition keeps only its path; a knob it cannot carry shows on a native one.
    static string Condition(BindingBase? binding)
    {
        if (binding is not Binding b)
            return $"binding={Describe(binding)}";

        var path = b.Path?.Path ?? "";

        // Read off its own element, DataContext is the trigger's own context: the compiled root.
        if (
            b.RelativeSource?.Mode == RelativeSourceMode.Self
            && path.StartsWith("DataContext.", StringComparison.Ordinal)
        )
            path = path.Substring("DataContext.".Length);

        var text = $"path={path}";
        if (b.Converter is not null)
            text += $" converter={b.Converter.GetType().Name}";
        if (b.ConverterParameter is not null)
            text += $" parameter={Describe(b.ConverterParameter)}";
        if (!string.IsNullOrEmpty(b.StringFormat))
            text += $" format={b.StringFormat}";
        if (b.Mode != BindingMode.Default)
            text += $" mode={b.Mode}";
        if (!IsUnset(b.FallbackValue))
            text += $" fallback={Describe(b.FallbackValue)}";
        if (!IsUnset(b.TargetNullValue))
            text += $" null={Describe(b.TargetNullValue)}";

        return text;
    }

    static string SourceSegment(DependencyProperty property)
    {
        var owner = OwnerOf(property);
        if (
            owner is null
            || owner.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance)
                is not null
        )
            return property.Name;

        return $"({OwnerName(owner)}.{property.Name})";
    }

    // An attached property keeps its owner, so two owners' same-named properties stay apart.
    static string PropertyName(DependencyProperty? property) =>
        property is null ? "" : SourceSegment(property);

    static string DescribeCompiledTrigger(CompiledTriggerSpec spec)
    {
        static string Condition(CompiledTriggerCondition condition)
        {
            var segments = new List<string>();
            if (condition.Part.SourceProperty is { } property)
                segments.Add(SourceSegment(property));

            segments.AddRange(condition.Part.Hops.Select(h => h.Name));
            return $"path={string.Join(".", segments)} value={Plain(condition.Value)}";
        }

        static string PropertyCondition(CompiledTriggerCondition condition) =>
            $"{PropertyName(condition.Part.SourceProperty)} source=- value={Plain(condition.Value)}";

        var block = new StringBuilder(
            spec.PropertyDriven
                ? spec.Conditions.Length == 1
                    ? $"Trigger {PropertyCondition(spec.Conditions[0])}"
                    : "MultiTrigger ["
                        + string.Join(", ", spec.Conditions.Select(PropertyCondition))
                        + "]"
                : spec.Conditions.Length == 1
                    ? $"DataTrigger {Condition(spec.Conditions[0])}"
                    : "MultiDataTrigger ["
                        + string.Join(", ", spec.Conditions.Select(Condition))
                        + "]"
        );

        foreach (var setter in spec.Setters)
        {
            block
                .Append('\n')
                .Append(
                    $"  Setter {PropertyName(setter.Property)} target={setter.TargetName ?? "-"} "
                        + $"value={Plain(setter.Value)}"
                );
            AppendHeld(block, setter.Value);
        }

        return block.ToString();
    }

    static string Plain(object? value) => value is null ? "null" : Text(value);

    static List<CompiledTriggerSet> CompiledSetsUnder(DependencyObject? root)
    {
        var sets = new List<CompiledTriggerSet>();
        void Walk(DependencyObject node, int depth)
        {
            // Ahead of the early return: a templated root carries the enclosing template's sets.
            if (node is FrameworkElement element)
                sets.AddRange(CompiledTriggerSet.SetsOf(element));

            // A nested templated control's triggers belong to its own template, listed there; so do those
            // of the data template a presenter realizes.
            if (
                (depth > 0 && node is Control { Template: not null })
                || node is ContentPresenter { Content: not UIElement }
            )
                return;

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i), depth + 1);
        }

        if (root is not null)
            Walk(root, 0);
        return sets;
    }

    // The host's own presenter comes first in the walk; what it realized is the template's root.
    static DependencyObject? PresentedRoot(DependencyObject host)
    {
        var count = VisualTreeHelper.GetChildrenCount(host);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(host, i);
            if (child is ContentPresenter presenter)
                return VisualTreeHelper.GetChildrenCount(presenter) > 0
                    ? VisualTreeHelper.GetChild(presenter, 0)
                    : null;

            if (PresentedRoot(child) is { } found)
                return found;
        }

        return null;
    }

    static Control HostFor(Type? targetType)
    {
        if (targetType is null || !typeof(Control).IsAssignableFrom(targetType))
            return new ContentControl();

        var host = Constructible(targetType) ? targetType : NearestConstructibleSubtype(targetType);
        return (Control)Activator.CreateInstance(host)!;
    }

    static bool Constructible(Type type) =>
        !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null;

    static Type NearestConstructibleSubtype(Type baseType) =>
        baseType
            .Assembly.GetExportedTypes()
            .Where(t => baseType.IsAssignableFrom(t) && Constructible(t))
            .OrderBy(t => DistanceFrom(baseType, t))
            .ThenBy(t => t.FullName, StringComparer.Ordinal)
            .FirstOrDefault()
        ?? throw new InvalidOperationException(
            $"No public control can host a {baseType.Name} template"
        );

    static int DistanceFrom(Type ancestor, Type type)
    {
        var distance = 0;
        for (var t = type; t != ancestor; t = t.BaseType!)
            distance++;

        return distance;
    }

    static Control RealizeTemplate(Control host, Action<Control> apply)
    {
        host.Width = 800;
        host.Height = 600;
        apply(host);
        Realize(host);
        return host;
    }

    static void Realize(FrameworkElement element)
    {
        var root = new Grid();
        root.Children.Add(element);

        var view = GUI.CreateView(root);
        view.SetSize(800, 600);
        for (var i = 0; i < Frames; i++)
            view.Update(i * 0.016);
        root.UpdateLayout();
    }

    static void WalkVisual(DependencyObject node, StringBuilder sb, int depth)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        if (depth > Deepest)
        {
            if (count > 0)
                sb.AppendLine(new string(' ', depth * 2) + Truncation);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            sb.AppendLine(new string(' ', depth * 2) + child.GetType().Name);
            DumpObject(child, sb, depth + 1, new HashSet<nint>());
            WalkVisual(child, sb, depth + 1);
        }
    }

    // A visual value is walked where it sits in the tree; expanding it here would count it twice.
    static void DumpObject(
        DependencyObject node,
        StringBuilder sb,
        int depth,
        HashSet<nint> expanding
    )
    {
        if (!expanding.Add(Handle(node)))
            return;

        var pad = new string(' ', depth * 2);
        foreach (var (name, text, value) in LocalValues(node))
        {
            sb.AppendLine(pad + $".{name} = {text}");
            if (value is DependencyObject nested and not Visual)
                DumpObject(nested, sb, depth + 1, expanding);
        }

        DumpParts(node, sb, depth, expanding);

        if (node is IEnumerable items and not Visual)
        {
            foreach (var item in items)
            {
                sb.AppendLine(pad + $"- {Describe(item)}");
                if (item is DependencyObject nested and not Visual)
                    DumpObject(nested, sb, depth + 1, expanding);
            }
        }

        expanding.Remove(Handle(node));
    }

    static void DumpParts(
        DependencyObject node,
        StringBuilder sb,
        int depth,
        HashSet<nint> expanding
    )
    {
        var pad = new string(' ', depth * 2);

        if (node is Grid grid)
        {
            DumpMembers("RowDefinitions", grid.RowDefinitions, sb, depth, expanding);
            DumpMembers("ColumnDefinitions", grid.ColumnDefinitions, sb, depth, expanding);
        }

        if (node is TextBlock text)
            DumpMembers("Inlines", text.Inlines, sb, depth, expanding);
        else if (node is Span span)
            DumpMembers("Inlines", span.Inlines, sb, depth, expanding);

        if (node is not FrameworkElement element)
            return;

        if (
            element.Resources is { } resources
            && (resources.Count > 0 || resources.MergedDictionaries.Count > 0)
        )
        {
            sb.AppendLine(pad + "Resources");
            DumpEntries(resources, Unnamed, sb, depth + 1);
        }

        if (element.Triggers is { Count: > 0 } triggers)
        {
            sb.AppendLine(pad + "Triggers");
            DumpTriggers(triggers, null, sb, depth + 1);
        }
    }

    static void DumpMembers<T>(
        string label,
        IList<T> members,
        StringBuilder sb,
        int depth,
        HashSet<nint> expanding
    )
        where T : DependencyObject
    {
        if (members.Count == 0)
            return;

        var pad = new string(' ', depth * 2);
        sb.AppendLine(pad + label);
        foreach (var member in members)
        {
            sb.AppendLine(pad + "  " + member.GetType().Name);
            DumpObject(member, sb, depth + 2, expanding);
        }
    }

    // Layout writes these, not the document.
    static readonly HashSet<string> Measured = new HashSet<string>(StringComparer.Ordinal)
    {
        "ActualWidthProperty",
        "ActualHeightProperty",
    };

    sealed class Slot(
        string name,
        DependencyProperty property,
        bool misrouted,
        PropertyInfo? accessor
    )
    {
        public string Name { get; } = name;
        public DependencyProperty Property { get; } = property;
        public bool Misrouted { get; } = misrouted;
        public PropertyInfo? NoesisAccessor { get; } = accessor;
    }

    static readonly Dictionary<Type, Slot[]> Slots = new Dictionary<Type, Slot[]>();

    static readonly Dictionary<Type, HashSet<nint>> Owned = new Dictionary<Type, HashSet<nint>>();

    static Slot[] SlotsOf(Type type)
    {
        if (Slots.TryGetValue(type, out var cached))
            return cached;

        var found = new List<Slot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (
                var member in t.GetMembers(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
                )
            )
            {
                if (
                    !member.Name.EndsWith("Property", StringComparison.Ordinal)
                    || Measured.Contains(member.Name)
                    || !seen.Add(member.Name)
                )
                    continue;

                DependencyProperty? property;
                Type registered;
                try
                {
                    property = member switch
                    {
                        PropertyInfo p when p.PropertyType == typeof(DependencyProperty) =>
                            (DependencyProperty?)p.GetValue(null),
                        FieldInfo f when f.FieldType == typeof(DependencyProperty) =>
                            (DependencyProperty?)f.GetValue(null),
                        _ => null,
                    };
                    if (property is null)
                        continue;

                    // Throws for a type Noesis never registered, which neither side can read.
                    registered = property.PropertyType;
                }
                catch (Exception)
                {
                    continue;
                }

                var misrouted = NoesisSlots.IsMisrouted(
                    (Nullable.GetUnderlyingType(registered) ?? registered).FullName!
                );
                var name = member.Name.Substring(0, member.Name.Length - "Property".Length);
                var accessor = misrouted
                    ? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(p =>
                            p.Name == name
                            && p.DeclaringType?.Assembly == typeof(DependencyObject).Assembly
                        )
                    : null;

                found.Add(new Slot(member.Name, property, misrouted, accessor));
            }
        }

        return Slots[type] = found.ToArray();
    }

    static HashSet<nint> OwnedBy(Type type)
    {
        if (!Owned.TryGetValue(type, out var owned))
            Owned[type] = owned = new HashSet<nint>(SlotsOf(type).Select(s => Handle(s.Property)));

        return owned;
    }

    static List<(string Name, string Text, object? Value)> LocalValues(DependencyObject target)
    {
        var results = new List<(string, string, object?)>();

        foreach (var slot in SlotsOf(target.GetType()))
        {
            (string Text, object? Value)? read;
            try
            {
                read = Read(target, slot);
            }
            catch (Exception)
            {
                continue;
            }

            if (read is { } found)
                results.Add((slot.Name, found.Text, found.Value));
        }

        foreach (var (property, value) in Attached(target))
        {
            if (value is not null && !IsUnset(value))
                results.Add(
                    ($"{OwnerName(OwnerOf(property))}.{property.Name}", Describe(value), value)
                );
        }

        results.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return results;
    }

    // The toolkit's own attached properties carry compiled wiring, which the parser never sets.
    static List<(DependencyProperty Property, object? Value)> Attached(DependencyObject target)
    {
        var stored = new List<(DependencyProperty Property, object? Value)>();
        target.EnumProperties((property, value) => stored.Add((property, value)));

        var type = target.GetType();
        var owned = OwnedBy(type);
        var seen = new HashSet<nint>();
        var attached = new List<(DependencyProperty, object?)>();
        foreach (var (property, value) in stored)
        {
            var handle = Handle(property);
            var owner = OwnerOf(property);
            if (
                !seen.Add(handle)
                || owned.Contains(handle)
                || owner?.IsAssignableFrom(type) == true
                || owner?.Assembly == typeof(CompiledTriggerSet).Assembly
            )
                continue;

            attached.Add((property, value));
        }

        return attached;
    }

    static Type? OwnerOf(DependencyProperty property)
    {
        try
        {
            return property.OwnerType;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string OwnerName(Type? owner) =>
        owner is null ? "?"
        : owner.Assembly == typeof(DependencyObject).Assembly ? owner.Name
        : owner.FullName ?? owner.Name;

    // GetValue takes a misrouted value for a pointer and crashes; only these three reads are safe.
    static (string Text, object? Value)? Read(DependencyObject target, Slot slot)
    {
        object? value;
        if (!slot.Misrouted)
            value = target.GetValue(slot.Property);
        else
        {
            try
            {
                value = DependencyRead.Value(target, slot.Property);
            }
            catch (NotSupportedException)
            {
                if (slot.NoesisAccessor is null)
                    return ("local:" + Describe(target.ReadLocalValue(slot.Property)), null);

                value = slot.NoesisAccessor.GetValue(target);
            }
        }

        return value is null || IsUnset(value) ? null : (Describe(value), value);
    }

    static bool IsUnset(object? value) => ReferenceEquals(value, DependencyProperty.UnsetValue);

    static string Knob(object? value) => value is null || IsUnset(value) ? "-" : Describe(value);

    static string Describe(object? value) =>
        value switch
        {
            null => "null",
            Binding binding =>
                $"Binding(Path={binding.Path?.Path}, Mode={binding.Mode}, Format={binding.StringFormat ?? "-"}, "
                    + $"Rel={(binding.RelativeSource is { } r ? $"{r.Mode}:{r.AncestorType?.Name}:{r.AncestorLevel}" : "-")}, "
                    + $"Element={binding.ElementName ?? "-"}, Source={Knob(binding.Source)}, "
                    + $"Converter={binding.Converter?.GetType().Name ?? "-"}, Parameter={Knob(binding.ConverterParameter)}, "
                    + $"Fallback={Knob(binding.FallbackValue)}, Null={Knob(binding.TargetNullValue)}, "
                    + $"Update={binding.UpdateSourceTrigger}, Delay={binding.Delay})",
            MultiBinding multi =>
                $"MultiBinding(Converter={multi.Converter?.GetType().Name ?? "-"}, Parameter={Knob(multi.ConverterParameter)}, "
                    + $"Mode={multi.Mode}, Format={multi.StringFormat ?? "-"}, Fallback={Knob(multi.FallbackValue)}, "
                    + $"Null={Knob(multi.TargetNullValue)}, Update={multi.UpdateSourceTrigger}, Delay={multi.Delay}, "
                    + $"[{string.Join(", ", multi.Bindings.Select(Describe))}])",
            BindingExpressionBase expression => Describe(expression.ParentBindingBase),
            SolidColorBrush brush => $"SolidColorBrush({brush.Color})",
            FontFamily family =>
                $"FontFamily({family.Source}, Base={family.BaseUri?.OriginalString ?? "-"})",
            DataTemplate => "DataTemplate",
            ControlTemplate => "ControlTemplate",
            ItemsPanelTemplate => "ItemsPanelTemplate",
            Style style =>
                $"Style({style.TargetType?.Name}, BasedOn={style.BasedOn?.TargetType?.Name ?? "-"})",
            _ => Text(value),
        };

    // An application's ToString may name the instance, so it differs between the two builds.
    static string Text(object value)
    {
        var type = value.GetType();
        var declaring = type.GetMethod("ToString", Type.EmptyTypes)?.DeclaringType?.Assembly;
        return
            declaring == typeof(object).Assembly
            || declaring == typeof(DependencyObject).Assembly
            || declaring?.GetName().Name?.StartsWith("System.", StringComparison.Ordinal) == true
            ? value.ToString() ?? ""
            : type.FullName ?? type.Name;
    }
}
