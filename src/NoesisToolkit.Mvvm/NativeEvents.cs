using System;
using System.Runtime.InteropServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Binds the toolkit's callback through the exports Noesis' own handler store uses; a delivery
/// allocates nothing, where Noesis' mints args per delivery and a store per element.</summary>
static unsafe class NativeEvents
{
    internal static readonly nint Loaded = BaseComponent
        .getCPtr(FrameworkElement.LoadedEvent)
        .Handle;
    internal static readonly nint Reloaded = BaseComponent
        .getCPtr(FrameworkElement.ReloadedEvent)
        .Handle;
    internal static readonly nint Unloaded = BaseComponent
        .getCPtr(FrameworkElement.UnloadedEvent)
        .Handle;

    internal static readonly uint DataContextChanged = NoesisInternals.EventId(
        null,
        "DataContextChanged"
    );
    internal static readonly uint LayoutUpdated = NoesisInternals.EventId(null, "LayoutUpdated");

    internal static void BindRouted(nint element, nint routedEvent) =>
        Noesis_RoutedEvent_Bind(&OnRoutedEvent, element, routedEvent);

    internal static void BindNamed(nint element, uint eventId) =>
        Noesis_Event_Bind(&OnEvent, element, eventId);

    internal static void UnbindRouted(nint element, nint routedEvent) =>
        Noesis_RoutedEvent_Unbind(&OnRoutedEvent, element, routedEvent);

    internal static void UnbindNamed(nint element, uint eventId) =>
        Noesis_Event_Unbind(&OnEvent, element, eventId);

    internal static Key KeyOf(nint args) => (Key)KeyGet(null, new HandleRef(null, args));

    internal static void MarkHandled(nint args) =>
        HandledSet(null, new HandleRef(null, args), true);

    [UnmanagedCallersOnly]
    static void OnRoutedEvent(nint cPtrType, nint cPtr, nint routedEvent, nint sender, nint e)
    {
        try
        {
            // The element's end arrives as a raise with neither sender nor args.
            if ((sender == IntPtr.Zero && e == IntPtr.Zero) || !NoesisInternals.Initialized(null))
                return;

            if (ElementState.Find(cPtr) is not { } element)
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

            if (ElementState.Find(cPtr) is not { } element)
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

    [System.Runtime.CompilerServices.UnsafeAccessor(
        System.Runtime.CompilerServices.UnsafeAccessorKind.StaticMethod,
        Name = "RoutedEventArgs_Handled_set"
    )]
    static extern void HandledSet(
        [System.Runtime.CompilerServices.UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")]
            object? owner,
        HandleRef args,
        bool handled
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

    [DllImport("Noesis")]
    static extern void Noesis_RoutedEvent_Unbind(
        delegate* unmanaged<nint, nint, nint, nint, nint, void> callback,
        nint element,
        nint routedEvent
    );

    [DllImport("Noesis")]
    static extern void Noesis_Event_Unbind(
        delegate* unmanaged<nint, nint, uint, nint, nint, void> callback,
        nint element,
        uint eventId
    );
}
