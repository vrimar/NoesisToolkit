using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>
/// Change notification for a <see cref="DependencyProperty"/>, which Noesis otherwise raises none of.
/// </summary>
/// <remarks>
/// <para>Two routes, chosen by who registered the property. A property declared in managed code has
/// metadata this library can read and rewrite, so it is watched by overriding that metadata with a
/// callback — process-global, uninstallable, and free per instance.</para>
/// <para>A property Noesis itself registered is never overridden. Its record carries callbacks the
/// managed side can neither read nor put back, so replacing it on the owner corrupts native state —
/// claiming <c>Noesis.Border.BorderThickness</c> on <c>Border</c> breaks layout for every Border in
/// the process — while overriding it on a subclass is inert on some properties and simply never
/// fires, which is worse than crashing because nothing says so. Such a property is watched instead
/// through a hidden attached property bound to it: a reader alongside the native record rather than
/// a replacement of it, which cannot crash and cannot silently go dead.</para>
/// <para>An override is process-global and cannot be uninstalled, so a property is claimed once and
/// fanned out to instances through a table this class owns.</para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DependencyWatcher
{
    static readonly Dictionary<DependencyProperty, PropertyMetadata> Claimed =
        new Dictionary<DependencyProperty, PropertyMetadata>();

    static readonly Dictionary<DependencyProperty, DependencyProperty> Probes =
        new Dictionary<DependencyProperty, DependencyProperty>();

    static readonly ConditionalWeakTable<DependencyObject, Subscriptions> Table =
        new ConditionalWeakTable<DependencyObject, Subscriptions>();

    static int _probes;

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

        if (NeedsProbe(property))
            subscriptions.Probe(target, property, ProbeFor(property));
        else
            Claim(property);

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

    static readonly System.Reflection.Assembly Engine = typeof(FrameworkElement).Assembly;

    static bool NeedsProbe(DependencyProperty property) => property.OwnerType.Assembly == Engine;

    // An attached property is not a member of the element, so its bare name resolves against
    // nothing and the probe would silently never fire.
    static string PathTo(DependencyProperty property) =>
        IsAttached(property) ? $"({property.OwnerType.Name}.{property.Name})" : property.Name;

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:DynamicallyAccessedMembers",
        Justification = "NeedsProbe admits only a property owned by the Noesis assembly, and a "
            + "Noesis application has to root that assembly whole because the engine resolves XAML "
            + "types reflectively at load."
    )]
    static bool IsAttached(DependencyProperty property) =>
        property.OwnerType.GetProperty(
            property.Name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
        )
            is null
        && property.OwnerType.GetMethod(
            "Get" + property.Name,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        )
            is not null;

    static void Claim(DependencyProperty property)
    {
        if (Claimed.ContainsKey(property))
            return;

        var owner = property.OwnerType;
        var replacement = Replacement(property.GetMetadata(owner));
        Claimed[property] = replacement;
        property.OverrideMetadata(owner, replacement);
    }

    // An override supplies a whole record, so every flag the property was registered with has to be
    // carried across or layout silently stops invalidating on it. The default value is left unset
    // because the registered one survives an override that states none.
    static PropertyMetadata Replacement(PropertyMetadata? existing)
    {
        if (existing is not FrameworkPropertyMetadata framework)
            return new PropertyMetadata(OnChanged);

        return new FrameworkPropertyMetadata(OnChanged)
        {
            AffectsMeasure = framework.AffectsMeasure,
            AffectsArrange = framework.AffectsArrange,
            AffectsParentMeasure = framework.AffectsParentMeasure,
            AffectsParentArrange = framework.AffectsParentArrange,
            AffectsRender = framework.AffectsRender,
            Inherits = framework.Inherits,
            OverridesInheritanceBehavior = framework.OverridesInheritanceBehavior,
            IsNotDataBindable = framework.IsNotDataBindable,
            BindsTwoWayByDefault = framework.BindsTwoWayByDefault,
            Journal = framework.Journal,
            SubPropertiesDoNotAffectRender = framework.SubPropertiesDoNotAffectRender,
            DefaultUpdateSourceTrigger = framework.DefaultUpdateSourceTrigger,
        };
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
            target.SetBinding(
                probe,
                new Binding(PathTo(property)) { Source = target, Mode = BindingMode.OneWay }
            );
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
