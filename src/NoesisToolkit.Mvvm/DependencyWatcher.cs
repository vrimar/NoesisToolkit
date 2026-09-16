using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>
/// Change notification for a <see cref="DependencyProperty"/>, which Noesis otherwise raises none of.
/// </summary>
/// <remarks>
/// <para>Two routes, chosen by how the property was registered. One the <c>[DependencyProperty]</c>
/// generator registered carries a callback from <see cref="Notifying"/>, so it reports here from the
/// start at no cost per instance. Any other property is watched through a hidden attached property
/// bound to it: a reader alongside the property's record rather than a replacement of it.</para>
/// <para>Overriding the metadata instead would be free per instance, but it changes what the engine
/// does. On a property Noesis registered it replaces callbacks the managed side can neither read nor
/// put back, which corrupts native state for every instance in the process, and on any property it
/// lands on the owner that already holds metadata, which Noesis reports as a warning the parsed
/// document never raises.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DependencyWatcher
{
    static readonly Dictionary<DependencyProperty, DependencyProperty> Probes =
        new Dictionary<DependencyProperty, DependencyProperty>();

    static readonly ConditionalWeakTable<DependencyObject, Subscriptions> Table =
        new ConditionalWeakTable<DependencyObject, Subscriptions>();

    static readonly HashSet<DependencyProperty> Notifies = new HashSet<DependencyProperty>();

    static int _probes;

    /// <summary>Marks <paramref name="property"/> as one whose registered metadata already reports
    /// every change here, so watching it needs no probe bound to it.</summary>
    /// <param name="property">A property registered with a callback from <see cref="Notifying"/>.</param>
    /// <returns><paramref name="property"/>, so the call can wrap a registration.</returns>
    public static DependencyProperty SelfNotifying(DependencyProperty property)
    {
        Guard.NotNull(property, nameof(property));

        lock (Notifies)
            Notifies.Add(property);

        return property;
    }

    /// <summary>A change callback that runs <paramref name="inner"/> and then reports the change
    /// here, for a registration to carry from the start.</summary>
    /// <param name="inner">The property's own change callback, or null.</param>
    /// <returns>The callback to register the property with.</returns>
    public static PropertyChangedCallback Notifying(PropertyChangedCallback? inner) =>
        inner is null
            ? OnChanged
            : (target, e) =>
            {
                inner(target, e);
                OnChanged(target, e);
            };

    /// <summary>Calls <paramref name="handler"/> whenever <paramref name="property"/> changes on
    /// <paramref name="target"/>.</summary>
    /// <param name="target">The element to watch.</param>
    /// <param name="property">The property to watch on it.</param>
    /// <param name="handler">Runs after the value has changed.</param>
    /// <remarks>An object outside a live view raises nothing at all, so a watcher on a detached
    /// element only starts reporting once the tree it is in is shown.</remarks>
    public static void Watch(FrameworkElement target, DependencyProperty property, Action handler)
    {
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(property, nameof(property));
        Guard.NotNull(handler, nameof(handler));

        var subscriptions = Table.GetOrCreateValue(target);

        if (!AlreadyNotifies(property))
            subscriptions.Probe(target, property, ProbeFor(property));

        subscriptions.Add(property, handler);
    }

    /// <summary>Stops a <see cref="Watch"/> subscription.</summary>
    /// <param name="target">The element being watched.</param>
    /// <param name="property">The property being watched on it.</param>
    /// <param name="handler">The handler that was registered.</param>
    public static void Unwatch(FrameworkElement target, DependencyProperty property, Action handler)
    {
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(property, nameof(property));
        Guard.NotNull(handler, nameof(handler));

        if (Table.TryGetValue(target, out var subscriptions))
            subscriptions.Remove(target, property, handler);
    }

    static bool AlreadyNotifies(DependencyProperty property)
    {
        lock (Notifies)
            return Notifies.Contains(property);
    }

    static DependencyProperty ProbeFor(DependencyProperty property)
    {
        if (Probes.TryGetValue(property, out var probe))
            return probe;

        probe = DependencyProperty.RegisterAttached(
            "NtkWatch" + _probes++.ToString(System.Globalization.CultureInfo.InvariantCulture),
            typeof(object),
            typeof(DependencyWatcher),
            new PropertyMetadata(
                null,
                (target, _) =>
                {
                    if (Table.TryGetValue(target, out var subscriptions))
                        subscriptions.Fire(property);
                }
            )
        );

        Probes[property] = probe;
        return probe;
    }

    // Noesis swallows whatever escapes here, so a handler that throws would vanish without a trace.
    static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (Table.TryGetValue(target, out var subscriptions))
            subscriptions.Fire(e.Property);
    }

    sealed class Subscriptions
    {
        readonly List<Entry> _entries = new List<Entry>();
        readonly List<DependencyProperty> _probed = new List<DependencyProperty>();

        internal void Probe(
            FrameworkElement target,
            DependencyProperty property,
            DependencyProperty probe
        )
        {
            if (_probed.Contains(property))
                return;

            _probed.Add(property);
            target.SetBinding(probe, new Binding(property, target) { Mode = BindingMode.OneWay });
        }

        internal void Add(DependencyProperty property, Action handler)
        {
            foreach (var entry in _entries)
            {
                if (entry.Property == property && entry.Handler == handler)
                    return;
            }

            _entries.Add(new Entry(property, handler));
        }

        internal void Remove(FrameworkElement target, DependencyProperty property, Action handler)
        {
            var left = 0;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Property != property)
                    continue;

                if (_entries[i].Handler == handler)
                    _entries.RemoveAt(i);
                else
                    left++;
            }

            if (left > 0 || !_probed.Remove(property))
                return;

            if (Probes.TryGetValue(property, out var probe))
            {
                BindingOperations.ClearBinding(target, probe);
                target.ClearValue(probe);
            }
        }

        // A handler is free to watch or unwatch while it runs, so it never iterates the live list.
        internal void Fire(DependencyProperty property)
        {
            List<Action>? handlers = null;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Property == property)
                    (handlers ??= new List<Action>()).Add(_entries[i].Handler);
            }

            if (handlers is null)
                return;

            foreach (var handler in handlers)
                handler();
        }

        readonly struct Entry
        {
            internal Entry(DependencyProperty property, Action handler)
            {
                Property = property;
                Handler = handler;
            }

            internal DependencyProperty Property { get; }
            internal Action Handler { get; }
        }
    }
}
