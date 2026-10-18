using System;
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

    static string Dump(ResourceDictionary dictionary, Dictionary<nint, string> named)
    {
        var entries = new List<(string Text, object Key, ResourceDictionary Owner)>();
        var merged = new List<string>();
        Flatten(dictionary, named, entries, merged);

        var sb = new StringBuilder();
        foreach (var source in merged)
            sb.AppendLine($"MERGED {source}");

        foreach (var (text, key, owner) in entries.OrderBy(e => e.Text, StringComparer.Ordinal))
        {
            sb.AppendLine($"KEY {text}");
            DumpValue(owner[key], sb, 1);
        }

        return sb.ToString();
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
        foreach (var (name, text) in LocalValues(root))
            sb.AppendLine($"  .{name} = {text}");

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
            {
                sb.AppendLine(pad + "DataTemplate");
                var host = RealizeTemplate(
                    new ContentControl(),
                    h => ((ContentControl)h).ContentTemplate = template
                );
                DumpTriggers(template.Triggers, CompiledSetsUnder(host), sb, depth + 1);
                WalkVisual(host, sb, depth + 1);
                return;
            }
            case ControlTemplate control:
            {
                sb.AppendLine(
                    pad + $"ControlTemplate TargetType={control.TargetType?.Name ?? "-"}"
                );
                // A template applies only to a host of its TargetType; anywhere else it is ignored.
                var host = RealizeTemplate(HostFor(control.TargetType), h => h.Template = control);
                DumpTriggers(control.Triggers, CompiledSetsUnder(host), sb, depth + 1);
                WalkVisual(host, sb, depth + 1);
                return;
            }
            case Style style:
                if (style.CanSeal && !style.IsSealed)
                    style.Seal();
                sb.AppendLine(
                    pad
                        + $"Style TargetType={style.TargetType?.Name} BasedOn={(style.BasedOn is null ? "-" : "yes")} "
                        + $"Setters={style.Setters.Count}"
                );
                foreach (var setter in style.Setters)
                    sb.AppendLine(pad + "  " + DescribeSetter(setter));
                DumpTriggers(style.Triggers, null, sb, depth + 1);
                return;
            case DependencyObject node:
                sb.AppendLine(pad + node.GetType().Name);
                foreach (var (name, text) in LocalValues(node))
                    sb.AppendLine(pad + $"  .{name} = {text}");
                return;
            default:
                sb.AppendLine(pad + $"{value.GetType().Name} = {value}");
                return;
        }
    }

    static string DescribeSetter(SetterBase setterBase) =>
        setterBase is not Setter setter
            ? setterBase.GetType().Name
            : $"Setter {setter.Property?.Name} target={setter.TargetName ?? "-"} value={Describe(setter.Value)}";

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
                    MultiDataTrigger multi => multi.Setters,
                    _ => null,
                };

                if (setters is not null)
                    foreach (var setter in setters.OfType<Setter>())
                        block
                            .Append('\n')
                            .Append(
                                $"  Setter {setter.Property?.Name} target={setter.TargetName ?? "-"} "
                                    + $"value={Plain(setter.Value)}"
                            );

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
                $"Trigger {t.Property?.Name} source={(string.IsNullOrEmpty(t.SourceName) ? "-" : t.SourceName)} value={Plain(t.Value)}",
            DataTrigger d => $"DataTrigger {Condition(d.Binding)} value={Plain(d.Value)}",
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
            EventTrigger e => $"EventTrigger {e.RoutedEvent?.Name ?? "-"}",
            _ => trigger.GetType().Name,
        };

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
        if (!string.IsNullOrEmpty(b.StringFormat))
            text += $" format={b.StringFormat}";
        if (b.Mode != BindingMode.Default)
            text += $" mode={b.Mode}";

        return text;
    }

    static string DescribeCompiledTrigger(CompiledTriggerSpec spec)
    {
        static string Condition(CompiledTriggerCondition condition)
        {
            var segments = new List<string>();
            if (condition.Part.SourceProperty is { } property)
                segments.Add(property.Name);

            segments.AddRange(condition.Part.Hops.Select(h => h.Name));
            return $"path={string.Join(".", segments)} value={Plain(condition.Value)}";
        }

        // A parsed MultiTrigger lists neither conditions nor setters, so a compiled one cannot either.
        if (spec.PropertyDriven && spec.Conditions.Length != 1)
            return "MultiTrigger";

        var block = new StringBuilder(
            spec.PropertyDriven
                ? $"Trigger {spec.Conditions[0].Part.SourceProperty?.Name} source=- "
                    + $"value={Plain(spec.Conditions[0].Value)}"
            : spec.Conditions.Length == 1 ? $"DataTrigger {Condition(spec.Conditions[0])}"
            : "MultiDataTrigger [" + string.Join(", ", spec.Conditions.Select(Condition)) + "]"
        );

        foreach (var setter in spec.Setters)
            block
                .Append('\n')
                .Append(
                    $"  Setter {setter.Property.Name} target={setter.TargetName ?? "-"} "
                        + $"value={Plain(setter.Value)}"
                );

        return block.ToString();
    }

    static string Plain(object? value) => value?.ToString() ?? "null";

    static List<CompiledTriggerSet> CompiledSetsUnder(DependencyObject root)
    {
        var sets = new List<CompiledTriggerSet>();
        void Walk(DependencyObject node, int depth)
        {
            if (depth > 30)
                return;

            // Ahead of the early return: a templated root carries the enclosing template's sets.
            if (node is FrameworkElement element)
                sets.AddRange(CompiledTriggerSet.SetsOf(element));

            // A nested templated control's triggers belong to its own template, listed there.
            if (depth > 0 && node is Control { Template: not null })
                return;

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i), depth + 1);
        }

        Walk(root, 0);
        return sets;
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
        if (depth > 30)
            return;

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            sb.AppendLine(new string(' ', depth * 2) + child.GetType().Name);

            foreach (var (name, text) in LocalValues(child))
                sb.AppendLine(new string(' ', depth * 2 + 2) + $".{name} = {text}");

            WalkVisual(child, sb, depth + 1);
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

    static List<(string Name, string Value)> LocalValues(DependencyObject target)
    {
        var results = new List<(string, string)>();

        foreach (var slot in SlotsOf(target.GetType()))
        {
            string? text;
            try
            {
                text = Read(target, slot);
            }
            catch (Exception)
            {
                continue;
            }

            if (text is not null)
                results.Add((slot.Name, text));
        }

        results.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return results;
    }

    // GetValue takes a misrouted value for a pointer and crashes; only these three reads are safe.
    static string? Read(DependencyObject target, Slot slot)
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
                    return "local:" + Describe(target.ReadLocalValue(slot.Property));

                value = slot.NoesisAccessor.GetValue(target);
            }
        }

        return value is null || ReferenceEquals(value, DependencyProperty.UnsetValue)
            ? null
            : Describe(value);
    }

    static string Describe(object? value) =>
        value switch
        {
            null => "null",
            Binding binding =>
                $"Binding(Path={binding.Path?.Path}, Mode={binding.Mode}, Format={binding.StringFormat ?? "-"}, "
                    + $"Rel={(binding.RelativeSource is { } r ? $"{r.Mode}:{r.AncestorType?.Name}" : "-")}, "
                    + $"Element={binding.ElementName ?? "-"}, Converter={binding.Converter?.GetType().Name ?? "-"})",
            MultiBinding multi =>
                $"MultiBinding(Converter={multi.Converter?.GetType().Name ?? "-"}, Count={multi.Bindings.Count})",
            SolidColorBrush brush => $"SolidColorBrush({brush.Color})",
            FontFamily family =>
                $"FontFamily({family.Source}, Base={family.BaseUri?.OriginalString ?? "-"})",
            DataTemplate => "DataTemplate",
            ControlTemplate => "ControlTemplate",
            ItemsPanelTemplate => "ItemsPanelTemplate",
            Style style =>
                $"Style({style.TargetType?.Name}, BasedOn={style.BasedOn?.TargetType?.Name ?? "-"})",
            _ => value.ToString() ?? "",
        };
}
