using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

// Keyed by handle and holding no proxy: Noesis holds a native element's proxy weakly, and a held proxy pins its element.
sealed unsafe class ElementState
{
    static readonly Dictionary<nint, ElementState> Live = new Dictionary<nint, ElementState>();

    readonly WeakReference<DependencyObject> _proxy;
    nint _handle;

    internal DependencyWatcher.Listeners? Watchers;
    internal List<DependencyProperty>? Probed;
    internal ElementEvents.Entry? Events;
    internal List<CompiledTriggerSet>? TriggerSets;

    ElementState(nint handle, DependencyObject proxy)
    {
        _handle = handle;
        _proxy = new WeakReference<DependencyObject>(proxy);
    }

    internal bool Alive => _handle != IntPtr.Zero;

    internal nint Handle => _handle;

    internal DependencyObject? Object
    {
        get
        {
            if (_handle == IntPtr.Zero)
                return null;

            if (_proxy.TryGetTarget(out var proxy))
                return proxy;

            proxy = NoesisInternals.Proxy(null, _handle, false) as DependencyObject;
            if (proxy is not null)
                _proxy.SetTarget(proxy);

            return proxy;
        }
    }

    internal FrameworkElement? Element => Object as FrameworkElement;

    internal static ElementState Of(DependencyObject target)
    {
        var handle = BaseComponent.getCPtr(target).Handle;
        lock (Live)
        {
            if (Live.TryGetValue(handle, out var state))
                return state;

            state = new ElementState(handle, target);
            Live.Add(handle, state);
            Noesis_Dependency_Destroyed_Bind(&OnDestroyed, handle);
            return state;
        }
    }

    internal static ElementState? Find(nint handle)
    {
        lock (Live)
            return Live.TryGetValue(handle, out var state) ? state : null;
    }

    [UnmanagedCallersOnly]
    static void OnDestroyed(nint handle)
    {
        try
        {
            ElementState? state;
            lock (Live)
                Live.Remove(handle, out state);

            state?.End();
        }
        catch (Exception exception)
        {
            Error.UnhandledException(exception);
        }
    }

    // Raised from the object's destructor, so nothing here may call back into Noesis.
    void End()
    {
        _handle = IntPtr.Zero;
        var events = Events;

        Watchers = null;
        Probed = null;
        Events = null;
        TriggerSets = null;

        events?.OnEnded();
    }

    [DllImport("Noesis")]
    static extern void Noesis_Dependency_Destroyed_Bind(
        delegate* unmanaged<nint, void> callback,
        nint instance
    );
}
