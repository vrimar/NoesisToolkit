using System;
using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The element events bindings subscribe to, taken once per element and fanned out here.
/// Noesis keeps a named-event handler whose removal leaves another handler on the event, so the
/// one subscription taken here is dropped only when nothing listens, and a copy that removal left
/// behind is recognised by the event it fires for and never added beside.</summary>
static class ElementEvents
{
    static readonly ConditionalWeakTable<FrameworkElement, Entry> Table =
        new ConditionalWeakTable<FrameworkElement, Entry>();

    internal static void WatchDataContext(FrameworkElement element, IChangeListener listener) =>
        Table.GetOrCreateValue(element).DataContextChanged.Add(element, listener);

    internal static void UnwatchDataContext(FrameworkElement element, IChangeListener listener)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.DataContextChanged.Remove(element, listener);
    }

    internal static void WatchLayout(FrameworkElement element, IChangeListener listener) =>
        Table.GetOrCreateValue(element).LayoutUpdated.Add(element, listener);

    internal static void UnwatchLayout(FrameworkElement element, IChangeListener listener)
    {
        if (Table.TryGetValue(element, out var entry))
            entry.LayoutUpdated.Remove(element, listener);
    }

    /// <summary>Hears Loaded, Reloaded and Unloaded for as long as the element lives.</summary>
    internal static void WatchLifecycle(FrameworkElement element, ElementLifecycle lifecycle) =>
        Table.GetOrCreateValue(element).Lifecycle(element).Add(lifecycle);

    sealed class Entry
    {
        internal readonly NamedEvent DataContextChanged = new NamedEvent(
            static (element, handler) => element.DataContextChanged += handler.OnDataContextChanged,
            static (element, handler) => element.DataContextChanged -= handler.OnDataContextChanged
        );

        internal readonly NamedEvent LayoutUpdated = new NamedEvent(
            static (element, handler) => element.LayoutUpdated += handler.OnLayoutUpdated,
            static (element, handler) => element.LayoutUpdated -= handler.OnLayoutUpdated
        );

        HandlerList<ElementLifecycle>? _lifecycle;

        internal HandlerList<ElementLifecycle> Lifecycle(FrameworkElement element)
        {
            if (_lifecycle is null)
            {
                _lifecycle = new HandlerList<ElementLifecycle>();
                element.Loaded += OnLoaded;
                element.Reloaded += OnLoaded;
                element.Unloaded += OnUnloaded;
            }

            return _lifecycle;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            using var run = _lifecycle!.Start();
            while (run.Next(out var lifecycle))
                lifecycle.OnLoaded();
        }

        void OnUnloaded(object sender, RoutedEventArgs e)
        {
            using var run = _lifecycle!.Start();
            while (run.Next(out var lifecycle))
                lifecycle.OnUnloaded();
        }
    }

    /// <summary>One named-event subscription, held while anything listens. A removal Noesis
    /// ignored shows up as a raise while unbound, after which the subscription is kept for the
    /// element's life; a second copy raised for the same args is skipped.</summary>
    sealed class NamedEvent(
        Action<FrameworkElement, NamedEvent> subscribe,
        Action<FrameworkElement, NamedEvent> unsubscribe
    )
    {
        readonly HandlerList<IChangeListener> _listeners = new HandlerList<IChangeListener>();
        bool _bound;
        bool _stuck;
        object? _lastArgs;

        internal void Add(FrameworkElement element, IChangeListener listener)
        {
            _listeners.Add(listener);
            if (!_bound)
            {
                _bound = true;
                if (!_stuck)
                    subscribe(element, this);
            }
        }

        internal void Remove(FrameworkElement element, IChangeListener listener)
        {
            _listeners.Remove(listener);
            if (_listeners.Count > 0 || !_bound)
                return;

            _bound = false;
            if (!_stuck)
                unsubscribe(element, this);
        }

        internal void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
            Raise(e);

        internal void OnLayoutUpdated(object sender, Noesis.EventArgs e) => Raise(e);

        void Raise(object args)
        {
            if (ReferenceEquals(args, _lastArgs))
                return;

            _lastArgs = args;

            if (!_bound)
            {
                _stuck = true;
                return;
            }

            using var run = _listeners.Start();
            while (run.Next(out var listener))
                listener.Changed();
        }
    }
}
