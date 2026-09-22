using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The element events bindings subscribe to, bound natively so a delivery allocates nothing.</summary>
static class ElementEvents
{
    static readonly ConditionalWeakTable<FrameworkElement, Entry> Table =
        new ConditionalWeakTable<FrameworkElement, Entry>();

    // GetOrCreateValue would go through Activator for every element.
    static readonly ConditionalWeakTable<FrameworkElement, Entry>.CreateValueCallback CreateEntry =
        static _ => new Entry();

    static readonly Action<FrameworkElement> BindDataContext = NativeEvents.BindDataContext;
    static readonly Action<FrameworkElement> BindLayout = NativeEvents.BindLayout;

    internal static void WatchDataContext(FrameworkElement element, IChangeListener listener) =>
        Table.GetValue(element, CreateEntry).DataContextChanged.Add(element, listener);

    internal static void UnwatchDataContext(FrameworkElement element, IChangeListener listener)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.DataContextChangedIfAny?.Remove(listener);
    }

    internal static void WatchLayout(FrameworkElement element, IChangeListener listener) =>
        Table.GetValue(element, CreateEntry).LayoutUpdated.Add(element, listener);

    internal static void UnwatchLayout(FrameworkElement element, IChangeListener listener)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.LayoutUpdatedIfAny?.Remove(listener);
    }

    /// <summary>Hears Loaded, Reloaded and Unloaded for as long as the element lives.</summary>
    internal static void WatchLifecycle(FrameworkElement element, ElementLifecycle lifecycle) =>
        Table.GetValue(element, CreateEntry).Lifecycle(element).Add(lifecycle);

    internal static void DispatchLoaded(FrameworkElement element)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.OnLoaded();
    }

    internal static void DispatchUnloaded(FrameworkElement element)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.OnUnloaded();
    }

    internal static void Subscribe(
        FrameworkElement element,
        nint routed,
        uint named,
        Delegate handler
    )
    {
        var entry = Table.GetValue(element, CreateEntry);
        if (entry.Bind(routed, named))
        {
            if (routed != 0)
                NativeEvents.BindRouted(element, routed);
            else
                NativeEvents.BindNamed(element, named);
        }

        entry.Subscriptions.Add(new Subscription(routed, named, handler));
    }

    internal static void Unsubscribe(
        FrameworkElement element,
        nint routed,
        uint named,
        Delegate handler
    )
    {
        if (!Table.TryGetValue(element, out var entry) || entry.SubscriptionsIfAny is not { } list)
            return;

        using var run = list.Start();
        while (run.Next(out var subscription))
        {
            if (
                subscription.Routed == routed
                && subscription.Named == named
                && subscription.Handler.Equals(handler)
            )
            {
                list.Remove(subscription);
                return;
            }
        }
    }

    internal static void DispatchRouted(FrameworkElement element, nint routed, nint args)
    {
        if (Table.TryGetValue(element, out var entry) && entry.SubscriptionsIfAny is { } list)
            Raise(list, element, routed, 0, args);
    }

    internal static void DispatchNamed(FrameworkElement element, uint named)
    {
        if (Table.TryGetValue(element, out var entry) && entry.SubscriptionsIfAny is { } list)
            Raise(list, element, 0, named, 0);
    }

    static void Raise(
        HandlerList<Subscription> list,
        FrameworkElement element,
        nint routed,
        uint named,
        nint args
    )
    {
        using var run = list.Start();
        while (run.Next(out var subscription))
        {
            if (subscription.Routed != routed || subscription.Named != named)
                continue;

            switch (subscription.Handler)
            {
                case Action<FrameworkElement> plain:
                    plain(element);
                    break;
                case Action<FrameworkElement, Key> keyed:
                    keyed(element, NativeEvents.KeyOf(args));
                    break;
            }
        }
    }

    internal static void DispatchDataContextChanged(FrameworkElement element)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.DataContextChangedIfAny?.Raise();
    }

    internal static void DispatchLayoutUpdated(FrameworkElement element)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.LayoutUpdatedIfAny?.Raise();
    }

    sealed class Subscription(nint routed, uint named, Delegate handler)
    {
        internal nint Routed => routed;
        internal uint Named => named;
        internal Delegate Handler => handler;
    }

    sealed class Entry
    {
        NamedEvent? _dataContextChanged;
        NamedEvent? _layoutUpdated;
        HandlerList<ElementLifecycle>? _lifecycle;
        HandlerList<Subscription>? _subscriptions;
        List<(nint Routed, uint Named)>? _bound;

        internal HandlerList<Subscription> Subscriptions =>
            _subscriptions ??= new HandlerList<Subscription>();

        internal HandlerList<Subscription>? SubscriptionsIfAny => _subscriptions;

        // True the first time an event is asked for on this element, when the native bind is owed.
        internal bool Bind(nint routed, uint named)
        {
            _bound ??= new List<(nint, uint)>(2);
            if (_bound.Contains((routed, named)))
                return false;

            _bound.Add((routed, named));
            return true;
        }

        internal NamedEvent DataContextChanged =>
            _dataContextChanged ??= new NamedEvent(BindDataContext);

        internal NamedEvent? DataContextChangedIfAny => _dataContextChanged;

        internal NamedEvent LayoutUpdated => _layoutUpdated ??= new NamedEvent(BindLayout);

        internal NamedEvent? LayoutUpdatedIfAny => _layoutUpdated;

        internal HandlerList<ElementLifecycle> Lifecycle(FrameworkElement element)
        {
            if (_lifecycle is null)
            {
                _lifecycle = new HandlerList<ElementLifecycle>();
                NativeEvents.BindLifecycle(element);
            }

            return _lifecycle;
        }

        internal void OnLoaded()
        {
            if (_lifecycle is null)
                return;

            using var run = _lifecycle.Start();
            while (run.Next(out var lifecycle))
                lifecycle.OnLoaded();
        }

        internal void OnUnloaded()
        {
            if (_lifecycle is null)
                return;

            using var run = _lifecycle.Start();
            while (run.Next(out var lifecycle))
                lifecycle.OnUnloaded();
        }
    }

    /// <summary>One subscription, taken on the first listener and kept for the element's life.</summary>
    sealed class NamedEvent(Action<FrameworkElement> bind)
    {
        readonly HandlerList<IChangeListener> _listeners = new HandlerList<IChangeListener>();
        bool _bound;

        internal void Add(FrameworkElement element, IChangeListener listener)
        {
            _listeners.Add(listener);
            if (!_bound)
            {
                _bound = true;
                bind(element);
            }
        }

        internal void Remove(IChangeListener listener) => _listeners.Remove(listener);

        internal void Raise()
        {
            if (_listeners.Count == 0)
                return;

            using var run = _listeners.Start();
            while (run.Next(out var listener))
                listener.Changed();
        }
    }
}
