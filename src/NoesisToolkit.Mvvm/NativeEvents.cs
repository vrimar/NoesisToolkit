using System;
using System.Runtime.InteropServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Binds the toolkit's callback through the exports Noesis' own handler store uses; a delivery
/// allocates nothing, where Noesis' mints args per delivery and a store per element.</summary>
static unsafe class NativeEvents
{
    static readonly nint Loaded = BaseComponent.getCPtr(FrameworkElement.LoadedEvent).Handle;
    static readonly nint Reloaded = BaseComponent.getCPtr(FrameworkElement.ReloadedEvent).Handle;
    static readonly nint Unloaded = BaseComponent.getCPtr(FrameworkElement.UnloadedEvent).Handle;

    static readonly uint DataContextChanged = NoesisInternals.EventId(null, "DataContextChanged");
    static readonly uint LayoutUpdated = NoesisInternals.EventId(null, "LayoutUpdated");

    internal static void BindLifecycle(FrameworkElement element)
    {
        var handle = BaseComponent.getCPtr(element).Handle;
        Noesis_RoutedEvent_Bind(&OnRoutedEvent, handle, Loaded);
        Noesis_RoutedEvent_Bind(&OnRoutedEvent, handle, Reloaded);
        Noesis_RoutedEvent_Bind(&OnRoutedEvent, handle, Unloaded);
    }

    internal static void BindDataContext(FrameworkElement element) =>
        Noesis_Event_Bind(&OnEvent, BaseComponent.getCPtr(element).Handle, DataContextChanged);

    internal static void BindLayout(FrameworkElement element) =>
        Noesis_Event_Bind(&OnEvent, BaseComponent.getCPtr(element).Handle, LayoutUpdated);

    internal static void BindRouted(FrameworkElement element, nint routedEvent) =>
        Noesis_RoutedEvent_Bind(&OnRoutedEvent, BaseComponent.getCPtr(element).Handle, routedEvent);

    internal static void BindNamed(FrameworkElement element, uint eventId) =>
        Noesis_Event_Bind(&OnEvent, BaseComponent.getCPtr(element).Handle, eventId);

    internal static Key KeyOf(nint args) => (Key)KeyGet(null, new HandleRef(null, args));

    [UnmanagedCallersOnly]
    static void OnRoutedEvent(nint cPtrType, nint cPtr, nint routedEvent, nint sender, nint e)
    {
        try
        {
            // The element's end arrives as a raise with neither sender nor args.
            if ((sender == IntPtr.Zero && e == IntPtr.Zero) || !NoesisInternals.Initialized(null))
                return;

            if (NoesisInternals.Proxy(null, cPtrType, cPtr, false) is not FrameworkElement element)
                return;

            if (routedEvent == Unloaded)
                ElementEvents.DispatchUnloaded(element);
            else if (routedEvent == Loaded || routedEvent == Reloaded)
                ElementEvents.DispatchLoaded(element);

            ElementEvents.DispatchRouted(element, routedEvent, e);
        }
        catch (Exception exception)
        {
            Error.UnhandledException(exception);
        }
    }

    [UnmanagedCallersOnly]
    static void OnEvent(nint cPtrType, nint cPtr, uint eventId, nint sender, nint e)
    {
        try
        {
            if ((sender == IntPtr.Zero && e == IntPtr.Zero) || !NoesisInternals.Initialized(null))
                return;

            if (NoesisInternals.Proxy(null, cPtrType, cPtr, false) is not FrameworkElement element)
                return;

            if (eventId == DataContextChanged)
                ElementEvents.DispatchDataContextChanged(element);
            else if (eventId == LayoutUpdated)
                ElementEvents.DispatchLayoutUpdated(element);

            ElementEvents.DispatchNamed(element, eventId);
        }
        catch (Exception exception)
        {
            Error.UnhandledException(exception);
        }
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(
        System.Runtime.CompilerServices.UnsafeAccessorKind.StaticMethod,
        Name = "KeyEventArgs_Key_get"
    )]
    static extern int KeyGet(
        [System.Runtime.CompilerServices.UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")]
            object? owner,
        HandleRef args
    );

    [DllImport("Noesis")]
    static extern void Noesis_RoutedEvent_Bind(
        delegate* unmanaged<nint, nint, nint, nint, nint, void> callback,
        nint element,
        nint routedEvent
    );

    [DllImport("Noesis")]
    static extern void Noesis_Event_Bind(
        delegate* unmanaged<nint, nint, uint, nint, nint, void> callback,
        nint element,
        uint eventId
    );
}
