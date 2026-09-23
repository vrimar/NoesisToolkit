using System;
using System.Collections.Generic;
using System.ComponentModel;
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

    /// <summary>Metadata whose change callback runs <paramref name="inner"/> and then reports here,
    /// with one reused args object per change.</summary>
    /// <param name="defaultValue">The property's default.</param>
    /// <param name="options">The framework options to register with.</param>
    /// <param name="inner">The property's own change callback, or null.</param>
    /// <returns>The metadata to register the property with.</returns>
    /// <remarks>The args are valid for the callback's duration only; kept, they read null.</remarks>
    public static FrameworkPropertyMetadata Metadata(
        object? defaultValue,
        FrameworkPropertyMetadataOptions options,
        PropertyChangedCallback? inner
    ) => new NotifyingMetadata(defaultValue, options, inner);

    /// <summary>Calls <paramref name="handler"/> with <paramref name="target"/> whenever
    /// <paramref name="property"/> changes on it.</summary>
    /// <param name="target">The element to watch.</param>
    /// <param name="property">The property to watch on it.</param>
    /// <param name="handler">Runs after the value has changed; watching again with the same handler
    /// does nothing.</param>
    /// <remarks>An object outside a live view raises nothing at all, so a watcher on a detached
    /// element only starts reporting once the tree it is in is shown. The subscription lasts until
    /// the element is destroyed, as one on a Noesis event does, so a handler that holds the element or
    /// the control around it keeps both alive: take the element from the argument instead.</remarks>
    public static void Watch(
        FrameworkElement target,
        DependencyProperty property,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(property, nameof(property));
        Guard.NotNull(handler, nameof(handler));

        var state = ElementState.Of(target);
        Watch(state, property, new ElementListener(state, handler));
    }

    internal static void Watch(
        ElementState target,
        DependencyProperty property,
        IChangeListener listener
    )
    {
        if (!target.Alive)
            return;

        Probe(target, property);
        (target.Watchers ??= new Listeners()).Add(property, listener);
    }

    /// <summary>Stops a <see cref="Watch(FrameworkElement, DependencyProperty, Action{FrameworkElement})"/>
    /// subscription.</summary>
    /// <param name="target">The element being watched.</param>
    /// <param name="property">The property being watched on it.</param>
    /// <param name="handler">The handler that was registered.</param>
    public static void Unwatch(
        FrameworkElement target,
        DependencyProperty property,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(property, nameof(property));
        Guard.NotNull(handler, nameof(handler));

        if (ElementState.Find(BaseComponent.getCPtr(target).Handle) is { } state)
            Unwatch(state, property, new ElementListener(state, handler));
    }

    internal static void Unwatch(
        ElementState target,
        DependencyProperty property,
        IChangeListener listener
    ) => target.Watchers?.Remove(property, listener);

    // Equal by the action it wraps, so the unwatch finds the watch's entry.
    sealed class ElementListener(ElementState target, Action<FrameworkElement> handler)
        : IChangeListener
    {
        public void Changed()
        {
            if (target.Element is { } element)
                handler(element);
        }

        public override bool Equals(object? obj) =>
            obj is ElementListener other && other.Handler.Equals(handler);

        public override int GetHashCode() => handler.GetHashCode();

        Action<FrameworkElement> Handler => handler;
    }

    static bool AlreadyNotifies(DependencyProperty property)
    {
        lock (Notifies)
            return Notifies.Contains(property);
    }

    // The probe stays bound once no handler is left: a recycled container watches the same property
    // again on its next load, and binding it anew costs a native expression each time.
    static void Probe(ElementState target, DependencyProperty property)
    {
        if (AlreadyNotifies(property) || (target.Probed?.Contains(property) ?? false))
            return;

        if (target.Element is not { } element)
            return;

        (target.Probed ??= new List<DependencyProperty>()).Add(property);
        element.SetBinding(
            ProbeFor(property),
            new Binding(property)
            {
                RelativeSource = RelativeSource.Self,
                Mode = BindingMode.OneWay,
            }
        );
    }

    static DependencyProperty ProbeFor(DependencyProperty property) =>
        Probes.TryGetValue(property, out var probe) ? probe : RegisterProbe(property);

    static DependencyProperty RegisterProbe(DependencyProperty property)
    {
        var probe = DependencyProperty.RegisterAttached(
            "NtkWatch" + _probes++.ToString(System.Globalization.CultureInfo.InvariantCulture),
            typeof(object),
            typeof(DependencyWatcher),
            new NotifyingMetadata(null, FrameworkPropertyMetadataOptions.None, null, property)
        );

        Probes[property] = probe;
        return probe;
    }

    internal static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (ElementState.Find(BaseComponent.getCPtr(target).Handle) is { } state)
            Fire(state, e.Property);
    }

    // Noesis swallows whatever escapes here, so a handler that throws would vanish without a trace.
    internal static void OnChanged(
        nint target,
        DependencyProperty? reports,
        DependencyPropertyChangedEventArgs e
    )
    {
        if (ElementState.Find(target) is { } state)
            Fire(state, reports ?? e.Property);
    }

    static void Fire(ElementState state, DependencyProperty property) =>
        state.Watchers?.Fire(property);

    internal sealed class Listeners
    {
        readonly Dictionary<DependencyProperty, HandlerList<IChangeListener>> _handlers =
            new Dictionary<DependencyProperty, HandlerList<IChangeListener>>();

        internal void Add(DependencyProperty property, IChangeListener listener)
        {
            if (!_handlers.TryGetValue(property, out var listeners))
            {
                listeners = new HandlerList<IChangeListener>();
                _handlers[property] = listeners;
            }

            if (!listeners.Contains(listener))
                listeners.Add(listener);
        }

        internal void Remove(DependencyProperty property, IChangeListener listener)
        {
            if (_handlers.TryGetValue(property, out var listeners))
                listeners.Remove(listener);
        }

        internal void Fire(DependencyProperty property)
        {
            if (!_handlers.TryGetValue(property, out var listeners))
                return;

            using var run = listeners.Start();
            while (run.Next(out var listener))
                listener.Changed();
        }
    }
}
