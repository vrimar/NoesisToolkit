using System;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Mvvm;

/// <summary>Subscribes to an element's events without Noesis' handler store or the args object it
/// mints per delivery. The handler gets the element; what it needs off the event is read natively.</summary>
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

    /// <summary>Runs <paramref name="handler"/> whenever the named event, one Noesis raises by name
    /// such as <c>SizeChanged</c> or <c>IsVisibleChanged</c>, fires on <paramref name="element"/>.</summary>
    /// <param name="element">The element to listen on.</param>
    /// <param name="eventName">The event's name as its C# event is called.</param>
    /// <param name="handler">Takes the element the event fired on.</param>
    public static void On(
        FrameworkElement element,
        string eventName,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(eventName, nameof(eventName));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Subscribe(element, 0, NoesisInternals.EventId(null, eventName), handler);
    }

    /// <summary>Stops a subscription taken with <see cref="On(FrameworkElement, string, Action{FrameworkElement})"/>.</summary>
    /// <param name="element">The element listened on.</param>
    /// <param name="eventName">The event's name.</param>
    /// <param name="handler">The handler that was registered.</param>
    public static void Off(
        FrameworkElement element,
        string eventName,
        Action<FrameworkElement> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(eventName, nameof(eventName));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Unsubscribe(element, 0, NoesisInternals.EventId(null, eventName), handler);
    }

    /// <summary>Runs <paramref name="handler"/> with the key whenever the key event reaches
    /// <paramref name="element"/>.</summary>
    /// <param name="element">The element to listen on.</param>
    /// <param name="routedEvent"><see cref="UIElement.KeyDownEvent"/>, <see cref="UIElement.KeyUpEvent"/> or a preview of either.</param>
    /// <param name="handler">Takes the element and the key.</param>
    public static void OnKey(
        FrameworkElement element,
        RoutedEvent routedEvent,
        Action<FrameworkElement, Key> handler
    )
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(routedEvent, nameof(routedEvent));
        Guard.NotNull(handler, nameof(handler));
        ElementEvents.Subscribe(element, BaseComponent.getCPtr(routedEvent).Handle, 0, handler);
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
}
