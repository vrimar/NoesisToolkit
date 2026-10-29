using System;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Mvvm;

/// <summary>Subscribes to an element's events without Noesis' handler store or the args object it
/// mints per delivery. The handler gets the element; what it needs off the event is read natively.</summary>
/// <remarks>A subscription lasts until the element is destroyed, as one on a Noesis event does, and
/// subscribing the same handler again does nothing. A handler that holds the element or the control
/// around it keeps both alive: take the element from the argument instead.</remarks>
public static class Events
{
    /// <summary>Runs <paramref name="handler"/> whenever <paramref name="routedEvent"/> reaches
    /// <paramref name="element"/>.</summary>
    /// <param name="element">The element to listen on.</param>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">Takes the element the event reached.</param>
    public static void On(
        FrameworkElement element,
        RoutedEvent routedEvent,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Subscribe(element, BaseComponent.getCPtr(routedEvent).Handle, 0, handler);
    }

    /// <summary>Stops a subscription taken with <see cref="On(FrameworkElement, RoutedEvent, Action{FrameworkElement})"/>.</summary>
    /// <param name="element">The element listened on.</param>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">The handler that was registered.</param>
    public static void Off(
        FrameworkElement element,
        RoutedEvent routedEvent,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Unsubscribe(element, BaseComponent.getCPtr(routedEvent).Handle, 0, handler);
    }

    /// <summary>Runs <paramref name="handler"/> whenever <paramref name="routedEvent"/> reaches the
    /// element <paramref name="element"/> names; subscribing mints no proxy for it.</summary>
    /// <param name="element">The element to listen on, by handle.</param>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">Takes the element the event reached.</param>
    public static void On(
        ElementHandle element,
        RoutedEvent routedEvent,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        if (!element.IsFrameworkElement)
            throw new ArgumentException("The handle names no FrameworkElement.", nameof(element));

        ElementEvents.Subscribe(
            ElementState.Of(element.Pointer),
            BaseComponent.getCPtr(routedEvent).Handle,
            0,
            handler
        );
    }

    /// <summary>Stops a subscription taken with <see cref="On(ElementHandle, RoutedEvent, Action{FrameworkElement})"/>.</summary>
    /// <param name="element">The element listened on, by handle.</param>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">The handler that was registered.</param>
    public static void Off(
        ElementHandle element,
        RoutedEvent routedEvent,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Unsubscribe(
            element.Pointer,
            BaseComponent.getCPtr(routedEvent).Handle,
            0,
            handler
        );
    }

    /// <summary>Runs <paramref name="handler"/> whenever the named event, one Noesis raises by name
    /// such as <c>IsVisibleChanged</c> or <c>LayoutUpdated</c>, fires on <paramref name="element"/>.</summary>
    /// <param name="element">The element to listen on.</param>
    /// <param name="eventName">The event's name as its C# event is called.</param>
    /// <param name="handler">Takes the element the event fired on.</param>
    /// <exception cref="ArgumentException">Noesis raises no event by that name, as for a routed event
    /// such as <c>SizeChanged</c>, which the <see cref="RoutedEvent"/> overload takes.</exception>
    public static void On(
        FrameworkElement element,
        string eventName,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(eventName, nameof(eventName));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Subscribe(element, 0, NamedEventId(eventName), handler);
    }

    /// <summary>Stops a subscription taken with <see cref="On(FrameworkElement, string, Action{FrameworkElement})"/>.</summary>
    /// <param name="element">The element listened on.</param>
    /// <param name="eventName">The event's name.</param>
    /// <param name="handler">The handler that was registered.</param>
    /// <exception cref="ArgumentException">Noesis raises no event by that name.</exception>
    public static void Off(
        FrameworkElement element,
        string eventName,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(eventName, nameof(eventName));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Unsubscribe(element, 0, NamedEventId(eventName), handler);
    }

    /// <summary>Runs <paramref name="handler"/> with the key whenever the key event reaches
    /// <paramref name="element"/>.</summary>
    /// <param name="element">The element to listen on.</param>
    /// <param name="routedEvent"><see cref="UIElement.KeyDownEvent"/>, <see cref="UIElement.KeyUpEvent"/> or a preview of either.</param>
    /// <param name="handler">Takes the element and the key.</param>
    /// <exception cref="ArgumentException"><paramref name="routedEvent"/> is not a key event.</exception>
    public static void OnKey(
        FrameworkElement element,
        RoutedEvent routedEvent,
        Action<FrameworkElement, Key> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Subscribe(element, KeyEvent(routedEvent), 0, handler);
    }

    /// <summary>Stops a subscription taken with <see cref="OnKey"/>.</summary>
    /// <param name="element">The element listened on.</param>
    /// <param name="routedEvent">The key event.</param>
    /// <param name="handler">The handler that was registered.</param>
    public static void OffKey(
        FrameworkElement element,
        RoutedEvent routedEvent,
        Action<FrameworkElement, Key> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Unsubscribe(element, BaseComponent.getCPtr(routedEvent).Handle, 0, handler);
    }

    /// <summary>Runs <paramref name="handler"/> with the key whenever the key event reaches
    /// <paramref name="element"/>, and marks the event handled when it returns true.</summary>
    /// <remarks>A handled key goes no further: handlers further along the route skip it, and so does
    /// the view's own keyboard navigation once the route ends.</remarks>
    /// <param name="element">The element to listen on.</param>
    /// <param name="routedEvent"><see cref="UIElement.KeyDownEvent"/>, <see cref="UIElement.KeyUpEvent"/> or a preview of either.</param>
    /// <param name="handler">Takes the element and the key; returns whether it handled the key.</param>
    /// <exception cref="ArgumentException"><paramref name="routedEvent"/> is not a key event.</exception>
    public static void OnKeyClaim(
        FrameworkElement element,
        RoutedEvent routedEvent,
        Func<FrameworkElement, Key, bool> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Subscribe(element, KeyEvent(routedEvent), 0, handler);
    }

    /// <summary>Stops a subscription taken with <see cref="OnKeyClaim"/>.</summary>
    /// <param name="element">The element listened on.</param>
    /// <param name="routedEvent">The key event.</param>
    /// <param name="handler">The handler that was registered.</param>
    public static void OffKeyClaim(
        FrameworkElement element,
        RoutedEvent routedEvent,
        Func<FrameworkElement, Key, bool> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Unsubscribe(element, BaseComponent.getCPtr(routedEvent).Handle, 0, handler);
    }

    static readonly Action AnyHandler = static () => { };

    static readonly KeyEventHandler AnyKeyHandler = static (_, _) => { };

    static nint KeyEvent(RoutedEvent routedEvent)
    {
        var handle = BaseComponent.getCPtr(routedEvent).Handle;
        bool keyed;
        try
        {
            keyed = NoesisInternals.IsLegalHandler(null, handle, AnyKeyHandler);
        }
        catch (InvalidOperationException)
        {
            keyed = false;
        }

        if (!keyed)
            throw new ArgumentException(
                $"{routedEvent.Name} is not a key event; its args carry no key.",
                nameof(routedEvent)
            );

        return handle;
    }

    // Bound by a name Noesis raises nothing under, a subscription is taken and never fires.
    static uint NamedEventId(string eventName)
    {
        var id = NoesisInternals.EventId(null, eventName);
        try
        {
            NoesisInternals.IsLegalHandler(null, id, AnyHandler);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentNullException)
        {
            throw new ArgumentException(
                $"Noesis raises no event named {eventName}; a routed event takes its RoutedEvent.",
                nameof(eventName),
                e
            );
        }

        return id;
    }
}
