using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Noesis internals, reached through .NET 10 accessors.</summary>
static class NoesisInternals
{
    // Keyed by native type: a native element's proxy is minted anew after every collection.
    static readonly Dictionary<nint, bool> FrameworkTypes = new Dictionary<nint, bool>();

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetProxy")]
    internal static extern object? Proxy(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? owner,
        nint cPtr,
        bool ownMemory
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetExtendInstance")]
    internal static extern object? ExtendInstance(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? owner,
        nint cPtr
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetInstanceHandle")]
    internal static extern HandleRef InstanceHandle(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? owner,
        object? instance
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "get_Initialized")]
    internal static extern bool Initialized(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? owner
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetEventId")]
    internal static extern uint EventId(
        [UnsafeAccessorType("Noesis.EventManager, Noesis.GUI")] object? owner,
        string name
    );

    internal static object? DataContext(nint element) =>
        Proxy(null, DataContextOf(null, new HandleRef(null, element)), false);

    /// <summary>The control <paramref name="element"/>'s template was applied to, or 0.</summary>
    internal static nint TemplatedParent(nint element) =>
        TemplatedParentOf(null, new HandleRef(null, element));

    /// <summary>The element registered under <paramref name="name"/> in the scope
    /// <paramref name="element"/> answers for, or 0 where none is or it is not a
    /// <see cref="FrameworkElement"/>.</summary>
    internal static nint FindElement(nint element, string name)
    {
        var found = FindName(null, new HandleRef(null, element), name);
        return found != 0 && IsFrameworkElement(found) ? found : 0;
    }

    internal static void ClearValue(nint target, DependencyProperty property) =>
        ClearValueHelper(
            null,
            new HandleRef(null, target),
            new HandleRef(null, BaseComponent.getCPtr(property).Handle)
        );

    static bool IsFrameworkElement(nint component)
    {
        var type = DynamicType(null, component);
        lock (FrameworkTypes)
        {
            if (!FrameworkTypes.TryGetValue(type, out var framework))
                FrameworkTypes[type] = framework =
                    Proxy(null, component, false) is FrameworkElement;

            return framework;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetDynamicType")]
    static extern nint DynamicType(BaseComponent? owner, nint cPtr);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FrameworkElement_DataContext_get")]
    static extern nint DataContextOf(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef element
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FrameworkElement_TemplatedParent_get")]
    static extern nint TemplatedParentOf(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef element
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "FrameworkElement_FindName")]
    static extern nint FindName(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef element,
        string name
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "DependencyObject_ClearValueHelper")]
    static extern void ClearValueHelper(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef target,
        HandleRef property
    );
}
