using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Noesis;

namespace NoesisToolkit.Mvvm;

/// <summary>Reads an element's geometry without the object Noesis' managed layer mints per read.
/// Its marshaller cannot treat <see cref="Point"/> or <see cref="Size"/> as blittable, because both
/// declare a marshalling attribute on each field, so it boxes one per call — which a panel pays for
/// every child, every layout pass.</summary>
public static class ElementGeometry
{
    /// <summary>The same as <see cref="UIElement.TranslatePoint"/>, without the allocation.</summary>
    /// <param name="element">The element the point is stated in.</param>
    /// <param name="point">The point, in <paramref name="element"/>'s own space.</param>
    /// <param name="relativeTo">The element to state the result in.</param>
    /// <returns>The point in <paramref name="relativeTo"/>'s space.</returns>
    public static unsafe Point TranslatePoint(UIElement element, Point point, UIElement relativeTo)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(relativeTo, nameof(relativeTo));

        var translated = Translate(
            null,
            BaseComponent.getCPtr(element),
            ref point,
            BaseComponent.getCPtr(relativeTo)
        );

        return translated == 0 ? default : Unsafe.Read<Point>((void*)translated);
    }

    /// <summary>The same as <see cref="Visual.PointToScreen"/>, without the allocation.</summary>
    /// <param name="visual">The visual the point is stated in.</param>
    /// <param name="point">The point, in <paramref name="visual"/>'s own space.</param>
    /// <returns>The point in screen space.</returns>
    public static unsafe Point PointToScreen(Visual visual, Point point)
    {
        Guard.NotNull(visual, nameof(visual));

        var screen = ToScreen(null, BaseComponent.getCPtr(visual), ref point);
        return screen == 0 ? default : Unsafe.Read<Point>((void*)screen);
    }

    /// <summary>The same as <see cref="Visual.PointFromScreen"/>, without the allocation.</summary>
    /// <param name="visual">The visual to state the result in.</param>
    /// <param name="point">The point, in screen space.</param>
    /// <returns>The point in <paramref name="visual"/>'s own space.</returns>
    public static unsafe Point PointFromScreen(Visual visual, Point point)
    {
        Guard.NotNull(visual, nameof(visual));

        var local = FromScreen(null, BaseComponent.getCPtr(visual), ref point);
        return local == 0 ? default : Unsafe.Read<Point>((void*)local);
    }

    /// <summary>The same as <see cref="UIElement.DesiredSize"/>, without the allocation.</summary>
    /// <param name="element">The element to measure.</param>
    /// <returns>What the element asked for in the last measure pass.</returns>
    public static unsafe Size DesiredSize(UIElement element)
    {
        Guard.NotNull(element, nameof(element));

        var size = Desired(null, BaseComponent.getCPtr(element));
        return size == 0 ? default : Unsafe.Read<Size>((void*)size);
    }

    /// <summary>The same as <see cref="UIElement.RenderSize"/>, without the allocation.</summary>
    /// <param name="element">The element to read.</param>
    /// <returns>What the element was arranged at.</returns>
    public static unsafe Size RenderSize(UIElement element)
    {
        Guard.NotNull(element, nameof(element));

        var size = Rendered(null, BaseComponent.getCPtr(element));
        return size == 0 ? default : Unsafe.Read<Size>((void*)size);
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "UIElement_TranslatePoint")]
    static extern nint Translate(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef element,
        ref Point point,
        HandleRef relativeTo
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Visual_PointToScreen")]
    static extern nint ToScreen(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef visual,
        ref Point point
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Visual_PointFromScreen")]
    static extern nint FromScreen(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef visual,
        ref Point point
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "UIElement_DesiredSize_get")]
    static extern nint Desired(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef element
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "UIElement_RenderSize_get")]
    static extern nint Rendered(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef element
    );
}
