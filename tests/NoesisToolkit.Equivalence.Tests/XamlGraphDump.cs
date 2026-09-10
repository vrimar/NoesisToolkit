using System.Reflection;
using System.Text;
using Noesis;
using Path = System.IO.Path;

namespace NoesisToolkit.Equivalence.Tests;

/// <summary>Renders a realized object graph as text, so a compiled dictionary can be compared with
/// the one the native parser builds. Templates are realized before walking: a template prototype
/// does not expose its children through the managed API.</summary>
internal static class XamlGraphDump
{
    /// <summary>The fixture corpus, copied next to the test binary.</summary>
    internal static string FixtureRoot() => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>What a logical name is resolved against: paths are project-relative, so Fixtures/ is part of them.</summary>
    internal static string ProviderRoot() => AppContext.BaseDirectory;

    /// <summary>Serves the fixture corpus, so a relative or pack-style Source= resolves off disk.</summary>
    internal sealed class FileXamlProvider(string root) : XamlProvider
    {
        public override Stream? LoadXaml(Uri uri)
        {
            var path = uri.ToString().Replace('\\', '/').TrimStart('/');
            var semi = path.IndexOf(';');
            var relative = semi < 0 ? path : path.Substring(semi + 1);

            var full = Path.Combine(root, relative);
            return File.Exists(full) ? File.OpenRead(full) : null;
        }
    }

    /// <summary>An x:Class root is not a dictionary entry: it is realized directly, and its own
    /// local values are part of the comparison because the root is where they are applied.</summary>
    internal static string Dump(FrameworkElement root)
    {
        var sb = new StringBuilder();
        sb.AppendLine(root.GetType().Name);
        foreach (var (name, text) in LocalValues(root))
            sb.AppendLine($"  .{name} = {text}");

        root.Width = 800;
        root.Height = 600;

        var host = new Grid();
        host.Children.Add(root);

        var view = GUI.CreateView(host);
        view.SetSize(800, 600);
        for (var i = 0; i < 6; i++)
            view.Update(i * 0.016);
        host.UpdateLayout();

        WalkVisual(root, sb, 1);
        return sb.ToString();
    }

    internal static string Dump(ResourceDictionary dictionary)
    {
        var sb = new StringBuilder();
        // A key routed through a merged dictionary resolves the same way, so flatten before diffing.
        var entries = new List<(string Text, object Key, ResourceDictionary Owner)>();
        Flatten(dictionary, entries);

        foreach (var (text, key, owner) in entries.OrderBy(e => e.Text, StringComparer.Ordinal))
        {
            sb.AppendLine($"KEY {text}");
            DumpValue(owner[key], sb, 1);
        }

        return sb.ToString();
    }

    static void Flatten(
        ResourceDictionary dictionary,
        List<(string Text, object Key, ResourceDictionary Owner)> into
    )
    {
        foreach (var key in dictionary.Keys)
            into.Add((key?.ToString() ?? "", key!, dictionary));

        foreach (var merged in dictionary.MergedDictionaries)
            Flatten(merged, into);
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
                DumpTriggers(template.Triggers, sb, depth + 1);
                DumpRealized(
                    new ContentControl(),
                    host => ((ContentControl)host).ContentTemplate = template,
                    sb,
                    depth + 1
                );
                return;
            case ControlTemplate control:
                sb.AppendLine(
                    pad + $"ControlTemplate TargetType={control.TargetType?.Name ?? "-"}"
                );
                DumpTriggers(control.Triggers, sb, depth + 1);
                // A template only applies to a host of its own TargetType; on anything else it is
                // silently ignored and the whole subtree compares as one blank line.
                DumpRealized(
                    HostFor(control.TargetType),
                    host => host.Template = control,
                    sb,
                    depth + 1
                );
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
                    sb.AppendLine(pad + "  " + DescribeSetter(setter));
                DumpTriggers(style.Triggers, sb, depth + 1);
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

    static void DumpTriggers(TriggerCollection? triggers, StringBuilder sb, int depth)
    {
        if (triggers is null || triggers.Count == 0)
            return;

        var pad = new string(' ', depth * 2);
        foreach (var trigger in triggers)
        {
            sb.AppendLine(pad + DescribeTrigger(trigger));
            if (trigger is Trigger simple)
                foreach (var setter in simple.Setters)
                    sb.AppendLine(pad + "  " + DescribeSetter(setter));
            else if (trigger is DataTrigger data)
                foreach (var setter in data.Setters)
                    sb.AppendLine(pad + "  " + DescribeSetter(setter));
        }
    }

    static string DescribeTrigger(TriggerBase trigger) =>
        trigger switch
        {
            Trigger t =>
                $"Trigger {t.Property?.Name} source={t.SourceName ?? "-"} value={Describe(t.Value)}",
            DataTrigger d => $"DataTrigger binding={Describe(d.Binding)} value={Describe(d.Value)}",
            EventTrigger e => $"EventTrigger {e.RoutedEvent?.Name ?? "-"}",
            _ => trigger.GetType().Name,
        };

    /// <summary>A host the template can actually apply to; a template names the type it styles.</summary>
    static Control HostFor(Type? targetType)
    {
        if (targetType is not null && typeof(Control).IsAssignableFrom(targetType))
        {
            try
            {
                if (Activator.CreateInstance(targetType) is Control control)
                    return control;
            }
            catch (Exception)
            {
                // Falls through to the generic host, which at least realizes nothing silently.
            }
        }

        return new ContentControl();
    }

    static void DumpRealized(Control host, Action<Control> apply, StringBuilder sb, int depth)
    {
        host.Width = 800;
        host.Height = 600;
        apply(host);

        var root = new Grid();
        root.Children.Add(host);

        var view = GUI.CreateView(root);
        view.SetSize(800, 600);
        for (var i = 0; i < 6; i++)
            view.Update(i * 0.016);
        root.UpdateLayout();

        WalkVisual(host, sb, depth);
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

    static List<(string Name, string Value)> LocalValues(DependencyObject target)
    {
        var results = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            foreach (
                var member in type.GetMembers(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
                )
            )
            {
                if (!member.Name.EndsWith("Property", StringComparison.Ordinal))
                    continue;
                if (!seen.Add(member.Name))
                    continue;

                DependencyProperty? property;
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
                }
                catch (Exception)
                {
                    continue;
                }

                if (property is null)
                    continue;

                object? value;
                try
                {
                    value = target.GetValue(property);
                }
                catch (Exception)
                {
                    continue;
                }

                if (value is null || ReferenceEquals(value, DependencyProperty.UnsetValue))
                    continue;

                results.Add((member.Name, Describe(value)));
            }
        }

        results.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return results;
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
