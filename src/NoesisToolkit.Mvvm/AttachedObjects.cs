using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Noesis;
using Noesis.Interactivity;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Mvvm;

/// <summary>Reads what a behavior or trigger is attached to, including while it detaches during a
/// teardown. Noesis releases the element's managed object before it detaches what the element
/// carries, so <see cref="AttachableObject.AssociatedObject"/> read there logs "Extend already
/// removed" and returns null.</summary>
public static class AttachedObjects
{
    /// <summary>The same as <see cref="AttachableObject.AssociatedObject"/>, without the error where
    /// Noesis has already released it.</summary>
    /// <param name="attachable">The behavior or trigger to read.</param>
    /// <returns>The object it is attached to, or null where it is not attached or that object is
    /// being torn down.</returns>
    public static DependencyObject? AssociatedObjectOf(AttachableObject attachable)
    {
        Guard.NotNull(attachable, nameof(attachable));

        var associated = Associated(null, BaseComponent.getCPtr(attachable));
        return associated == 0 || NoesisInternals.IsReleased(associated)
            ? null
            : NoesisInternals.Proxy(null, associated, false) as DependencyObject;
    }

    [UnsafeAccessor(
        UnsafeAccessorKind.StaticMethod,
        Name = "AttachableObject_AssociatedObject_get"
    )]
    static extern nint Associated(
        [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
        HandleRef attachable
    );
}
