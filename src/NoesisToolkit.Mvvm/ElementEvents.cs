using System;
using System.Collections.Generic;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The element events bindings subscribe to, bound natively so a delivery allocates nothing.</summary>
static class ElementEvents
{
    static readonly Action<nint> BindDataContext = NativeEvents.BindDataContext;
    static readonly Action<nint> BindLayout = NativeEvents.BindLayout;

    internal static void WatchDataContext(ElementState element, IChangeListener listener)
    {
        if (element.Alive)
            EntryOf(element).DataContextChanged.Add(element.Handle, listener);
    }

    internal static void UnwatchDataContext(ElementState element, IChangeListener listener) =>
        element.Events?.DataContextChangedIfAny?.Remove(listener);

    internal static void WatchLayout(ElementState element, IChangeListener listener)
    {
        if (element.Alive)
            EntryOf(element).LayoutUpdated.Add(element.Handle, listener);
    }

    internal static void UnwatchLayout(ElementState element, IChangeListener listener) =>
        element.Events?.LayoutUpdatedIfAny?.Remove(listener);

    /// <summary>Hears Loaded, Reloaded and Unloaded for as long as the element lives.</summary>
    internal static void WatchLifecycle(ElementState element, ElementLifecycle lifecycle)
    {
        if (element.Alive)
            EntryOf(element).Lifecycle(element.Handle).Add(lifecycle);
    }

    internal static void WatchRouted(ElementState element, nint routed, IChangeListener listener)
    {
        if (!element.Alive)
            return;

        var entry = EntryOf(element);
        Bind(entry, element.Handle, routed, 0);
        entry.Routed.Add(new RoutedWatch(routed, listener));
    }

    internal static void DispatchLoaded(ElementState element) => element.Events?.OnLoaded();

    internal static void DispatchUnloaded(ElementState element) => element.Events?.OnUnloaded();

    internal static void Subscribe(
        FrameworkElement element,
        nint routed,
        uint named,
        Delegate handler
    )
    {
        var state = ElementState.Of(element);
        var entry = EntryOf(state);
        Bind(entry, state.Handle, routed, named);

        var list = entry.Subscriptions;
        if (Find(list, routed, named, handler) is null)
            list.Add(new Subscription(routed, named, handler));
    }

    internal static void Unsubscribe(
        FrameworkElement element,
        nint routed,
        uint named,
        Delegate handler
    )
    {
        if (
            ElementState.Find(BaseComponent.getCPtr(element).Handle)?.Events?.SubscriptionsIfAny
                is { } list
            && Find(list, routed, named, handler) is { } subscription
        )
            list.Remove(subscription);
    }

    static Subscription? Find(
        HandlerList<Subscription> list,
        nint routed,
        uint named,
        Delegate handler
    )
    {
        using var run = list.Start();
        while (run.Next(out var subscription))
        {
            if (
                subscription.Routed == routed
                && subscription.Named == named
                && subscription.Handler.Equals(handler)
            )
                return subscription;
        }

        return null;
    }

    internal static void DispatchRouted(ElementState element, nint routed, nint args)
    {
        if (element.Events is not { } entry)
            return;

        entry.RaiseRouted(routed);
        if (entry.SubscriptionsIfAny is { } list)
            Raise(list, element, routed, 0, args);
    }

    internal static void DispatchNamed(ElementState element, uint named)
    {
        if (element.Events?.SubscriptionsIfAny is { } list)
            Raise(list, element, 0, named, 0);
    }

    // Resolved per matching handler only: a native element's proxy is minted anew after a collection.
    static void Raise(
        HandlerList<Subscription> list,
        ElementState state,
        nint routed,
        uint named,
        nint args
    )
    {
        using var run = list.Start();
        while (run.Next(out var subscription))
        {
            if (
                subscription.Routed != routed
                || subscription.Named != named
                || state.Element is not { } element
            )
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

    internal static void DispatchDataContextChanged(ElementState element) =>
        element.Events?.DataContextChangedIfAny?.Raise();

    internal static void DispatchLayoutUpdated(ElementState element) =>
        element.Events?.LayoutUpdatedIfAny?.Raise();

    static Entry EntryOf(ElementState element) => element.Events ??= new Entry();

    static void Bind(Entry entry, nint handle, nint routed, uint named)
    {
        if (!entry.Bind(routed, named))
            return;

        if (routed != 0)
            NativeEvents.BindRouted(handle, routed);
        else
            NativeEvents.BindNamed(handle, named);
    }

    internal sealed class Subscription(nint routed, uint named, Delegate handler)
    {
        internal nint Routed => routed;
        internal uint Named => named;
        internal Delegate Handler => handler;
    }

    internal sealed class RoutedWatch(nint routed, IChangeListener listener)
    {
        internal nint Routed => routed;
        internal IChangeListener Listener => listener;
    }

    internal sealed class Entry
    {
        NamedEvent? _dataContextChanged;
        NamedEvent? _layoutUpdated;
        HandlerList<ElementLifecycle>? _lifecycle;
        HandlerList<RoutedWatch>? _routed;
        HandlerList<Subscription>? _subscriptions;
        List<(nint Routed, uint Named)>? _bound;

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

        internal HandlerList<RoutedWatch> Routed => _routed ??= new HandlerList<RoutedWatch>();

        internal HandlerList<Subscription> Subscriptions =>
            _subscriptions ??= new HandlerList<Subscription>();

        internal HandlerList<Subscription>? SubscriptionsIfAny => _subscriptions;

        internal HandlerList<ElementLifecycle> Lifecycle(nint handle)
        {
            if (_lifecycle is null)
            {
                _lifecycle = new HandlerList<ElementLifecycle>();
                NativeEvents.BindLifecycle(handle);
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

        internal void OnEnded()
        {
            if (_lifecycle is null)
                return;

            using var run = _lifecycle.Start();
            while (run.Next(out var lifecycle))
                lifecycle.OnEnded();
        }

        internal void RaiseRouted(nint routed)
        {
            if (_routed is null)
                return;

            using var run = _routed.Start();
            while (run.Next(out var watch))
            {
                if (watch.Routed == routed)
                    watch.Listener.Changed();
            }
        }
    }

    /// <summary>One subscription, taken on the first listener and kept for the element's life.</summary>
    internal sealed class NamedEvent(Action<nint> bind)
    {
        readonly HandlerList<IChangeListener> _listeners = new HandlerList<IChangeListener>();
        bool _bound;

        internal void Add(nint element, IChangeListener listener)
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
