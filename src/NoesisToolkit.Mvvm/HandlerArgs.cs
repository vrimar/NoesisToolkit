using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Mvvm;

/// <summary>Hands every managed event handler one reused args object per args type and nesting depth.
/// Noesis mints a finalizable args object for each handler it delivers to, which a pointer move, a key
/// or a click pays once per subscribed element. The args are valid for the handler's duration only, as
/// Noesis' own are: a handler that keeps them reads a null native object afterwards.</summary>
public static class HandlerArgs
{
    /// <summary>Replaces Noesis' invoker for each args type below and leaves the rest as they are. Call it
    /// once Noesis is initialized; calling it again also takes events registered since.</summary>
    public static void Reuse()
    {
        RuntimeHelpers.RunClassConstructor(typeof(EventManager).TypeHandle);

        var table = (IDictionary)
            typeof(EventManager)
                .GetField("_handlerTypes", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;
        var info = typeof(EventManager).GetNestedType("HandlerInfo", BindingFlags.NonPublic)!;
        var delegateType = typeof(EventManager).GetNestedType(
            "InvokeHandlerDelegate",
            BindingFlags.NonPublic
        )!;
        var handlerType = info.GetProperty("Type")!;
        var invoker = info.GetProperty("Invoker")!;

        var keys = new List<object>(table.Count);
        foreach (DictionaryEntry entry in table)
            keys.Add(entry.Key);

        foreach (var key in keys)
        {
            var boxed = table[key]!;
            if (InvokerFor((Type)handlerType.GetValue(boxed)!) is not { } method)
                continue;

            invoker.SetValue(boxed, Delegate.CreateDelegate(delegateType, method));
            table[key] = boxed;
        }
    }

    static MethodInfo? InvokerFor(Type handler)
    {
        string? name =
            handler == typeof(RoutedEventHandler) ? nameof(DeliverRouted)
            : handler == typeof(MouseEventHandler) ? nameof(DeliverMouse)
            : handler == typeof(MouseButtonEventHandler) ? nameof(DeliverMouseButton)
            : handler == typeof(MouseWheelEventHandler) ? nameof(DeliverMouseWheel)
            : handler == typeof(QueryCursorEventHandler) ? nameof(DeliverQueryCursor)
            : handler == typeof(KeyEventHandler) ? nameof(DeliverKey)
            : handler == typeof(KeyboardFocusChangedEventHandler) ? nameof(DeliverKeyboardFocus)
            : handler == typeof(TextCompositionEventHandler) ? nameof(DeliverTextComposition)
            : handler == typeof(TextChangedEventHandler) ? nameof(DeliverTextChanged)
            : handler == typeof(ScrollChangedEventHandler) ? nameof(DeliverScrollChanged)
            : handler == typeof(SizeChangedEventHandler) ? nameof(DeliverSizeChanged)
            : handler == typeof(ToolTipEventHandler) ? nameof(DeliverToolTip)
            : handler == typeof(ContextMenuEventHandler) ? nameof(DeliverContextMenu)
            : handler == typeof(DependencyPropertyChangedEventHandler)
                ? nameof(DeliverPropertyChanged)
            : null;

        return name is null
            ? null
            : typeof(HandlerArgs).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
    }

    static object Sender(nint sender) => NoesisInternals.Proxy(null, sender, false)!;

    static void DeliverRouted(Delegate handler, nint sender, nint args)
    {
        var e = RoutedArgs.Borrow(args);
        try
        {
            ((RoutedEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            RoutedArgs.Return(e);
        }
    }

    static void DeliverMouse(Delegate handler, nint sender, nint args)
    {
        var e = MouseArgs.Borrow(args);
        try
        {
            ((MouseEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            MouseArgs.Return(e);
        }
    }

    static void DeliverMouseButton(Delegate handler, nint sender, nint args)
    {
        var e = MouseButtonArgs.Borrow(args);
        try
        {
            ((MouseButtonEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            MouseButtonArgs.Return(e);
        }
    }

    static void DeliverMouseWheel(Delegate handler, nint sender, nint args)
    {
        var e = MouseWheelArgs.Borrow(args);
        try
        {
            ((MouseWheelEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            MouseWheelArgs.Return(e);
        }
    }

    static void DeliverQueryCursor(Delegate handler, nint sender, nint args)
    {
        var e = QueryCursorArgs.Borrow(args);
        try
        {
            ((QueryCursorEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            QueryCursorArgs.Return(e);
        }
    }

    static void DeliverKey(Delegate handler, nint sender, nint args)
    {
        var e = KeyArgs.Borrow(args);
        try
        {
            ((KeyEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            KeyArgs.Return(e);
        }
    }

    static void DeliverKeyboardFocus(Delegate handler, nint sender, nint args)
    {
        var e = KeyboardFocusArgs.Borrow(args);
        try
        {
            ((KeyboardFocusChangedEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            KeyboardFocusArgs.Return(e);
        }
    }

    static void DeliverTextComposition(Delegate handler, nint sender, nint args)
    {
        var e = TextCompositionArgs.Borrow(args);
        try
        {
            ((TextCompositionEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            TextCompositionArgs.Return(e);
        }
    }

    static void DeliverTextChanged(Delegate handler, nint sender, nint args)
    {
        var e = TextChangedArgs.Borrow(args);
        try
        {
            ((TextChangedEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            TextChangedArgs.Return(e);
        }
    }

    static void DeliverScrollChanged(Delegate handler, nint sender, nint args)
    {
        var e = ScrollChangedArgs.Borrow(args);
        try
        {
            ((ScrollChangedEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            ScrollChangedArgs.Return(e);
        }
    }

    static void DeliverSizeChanged(Delegate handler, nint sender, nint args)
    {
        var e = SizeChangedArgs.Borrow(args);
        try
        {
            ((SizeChangedEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            SizeChangedArgs.Return(e);
        }
    }

    static void DeliverToolTip(Delegate handler, nint sender, nint args)
    {
        var e = ToolTipArgs.Borrow(args);
        try
        {
            ((ToolTipEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            ToolTipArgs.Return(e);
        }
    }

    static void DeliverContextMenu(Delegate handler, nint sender, nint args)
    {
        var e = ContextMenuArgs.Borrow(args);
        try
        {
            ((ContextMenuEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            ContextMenuArgs.Return(e);
        }
    }

    static void DeliverPropertyChanged(Delegate handler, nint sender, nint args)
    {
        var e = PropertyChangedArgs.Borrow(args);
        try
        {
            ((DependencyPropertyChangedEventHandler)handler)?.Invoke(Sender(sender), e);
        }
        finally
        {
            PropertyChangedArgs.Return(e);
        }
    }

    // A handler that raises an event of its own type re-enters before returning.
    sealed class Pool<T>(Func<T> create, Action<T, nint> point)
        where T : class
    {
        // Filled up front, so a focus change nested in a click nested in a key never grows it mid-frame.
        const int Primed = 3;

        readonly List<T> _args = Fill(create);
        int _depth;

        static List<T> Fill(Func<T> create)
        {
            var args = new List<T>(Primed);
            for (var i = 0; i < Primed; i++)
                args.Add(Fresh(create));

            return args;
        }

        static T Fresh(Func<T> create)
        {
            var fresh = create();
            GC.SuppressFinalize(fresh);
            return fresh;
        }

        internal T Borrow(nint native)
        {
            if (_depth == _args.Count)
                _args.Add(Fresh(create));

            var args = _args[_depth++];
            point(args, native);
            return args;
        }

        internal void Return(T args)
        {
            point(args, IntPtr.Zero);
            _depth--;
        }
    }

    static readonly Pool<RoutedEventArgs> RoutedArgs = new(() => NewRouted(0, false), Point);
    static readonly Pool<MouseEventArgs> MouseArgs = new(() => NewMouse(0, false), Point);
    static readonly Pool<MouseButtonEventArgs> MouseButtonArgs = new(
        () => NewMouseButton(0, false),
        Point
    );
    static readonly Pool<MouseWheelEventArgs> MouseWheelArgs = new(
        () => NewMouseWheel(0, false),
        Point
    );
    static readonly Pool<QueryCursorEventArgs> QueryCursorArgs = new(
        () => NewQueryCursor(0, false),
        Point
    );
    static readonly Pool<KeyEventArgs> KeyArgs = new(() => NewKey(0, false), Point);
    static readonly Pool<KeyboardFocusChangedEventArgs> KeyboardFocusArgs = new(
        () => NewKeyboardFocus(0, false),
        Point
    );
    static readonly Pool<TextCompositionEventArgs> TextCompositionArgs = new(
        () => NewTextComposition(0, false),
        Point
    );
    static readonly Pool<TextChangedEventArgs> TextChangedArgs = new(
        () => NewTextChanged(0, false),
        Point
    );
    static readonly Pool<ScrollChangedEventArgs> ScrollChangedArgs = new(
        () => NewScrollChanged(0, false),
        Point
    );
    static readonly Pool<SizeChangedEventArgs> SizeChangedArgs = new(
        () => NewSizeChanged(0, false),
        Point
    );
    static readonly Pool<ToolTipEventArgs> ToolTipArgs = new(() => NewToolTip(0, false), Point);
    static readonly Pool<ContextMenuEventArgs> ContextMenuArgs = new(
        () => NewContextMenu(0, false),
        Point
    );
    static readonly Pool<DependencyPropertyChangedEventArgs> PropertyChangedArgs = new(
        () => NewPropertyChanged(0, false),
        Point
    );

    // Each class in Noesis' hierarchy keeps its own copy of the native pointer.
    static void Point(RoutedEventArgs e, nint p)
    {
        Ptr((Noesis.EventArgs)e) = new HandleRef(e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(InputEventArgs e, nint p)
    {
        Point((RoutedEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(MouseEventArgs e, nint p)
    {
        Point((InputEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(MouseButtonEventArgs e, nint p)
    {
        Point((MouseEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(MouseWheelEventArgs e, nint p)
    {
        Point((MouseEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(QueryCursorEventArgs e, nint p)
    {
        Point((MouseEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(KeyboardEventArgs e, nint p)
    {
        Point((InputEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(KeyEventArgs e, nint p)
    {
        Point((KeyboardEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(KeyboardFocusChangedEventArgs e, nint p)
    {
        Point((KeyboardEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(TextCompositionEventArgs e, nint p)
    {
        Point((InputEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(TextChangedEventArgs e, nint p)
    {
        Point((RoutedEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(ScrollChangedEventArgs e, nint p)
    {
        Point((RoutedEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(SizeChangedEventArgs e, nint p)
    {
        Point((RoutedEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(ToolTipEventArgs e, nint p)
    {
        Point((RoutedEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(ContextMenuEventArgs e, nint p)
    {
        Point((RoutedEventArgs)e, p);
        Ptr(e) = new HandleRef(e, p);
    }

    static void Point(DependencyPropertyChangedEventArgs e, nint p) => Ptr(e) = new HandleRef(e, p);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(Noesis.EventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(RoutedEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(InputEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(MouseEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(MouseButtonEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(MouseWheelEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(QueryCursorEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(KeyboardEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(KeyEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(KeyboardFocusChangedEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(TextCompositionEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(TextChangedEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(ScrollChangedEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(SizeChangedEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(ToolTipEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(ContextMenuEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "swigCPtr")]
    static extern ref HandleRef Ptr(DependencyPropertyChangedEventArgs e);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern RoutedEventArgs NewRouted(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern MouseEventArgs NewMouse(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern MouseButtonEventArgs NewMouseButton(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern MouseWheelEventArgs NewMouseWheel(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern QueryCursorEventArgs NewQueryCursor(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern KeyEventArgs NewKey(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern KeyboardFocusChangedEventArgs NewKeyboardFocus(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern TextCompositionEventArgs NewTextComposition(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern TextChangedEventArgs NewTextChanged(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern ScrollChangedEventArgs NewScrollChanged(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern SizeChangedEventArgs NewSizeChanged(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern ToolTipEventArgs NewToolTip(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern ContextMenuEventArgs NewContextMenu(nint cPtr, bool cMemoryOwn);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    static extern DependencyPropertyChangedEventArgs NewPropertyChanged(nint cPtr, bool cMemoryOwn);
}
