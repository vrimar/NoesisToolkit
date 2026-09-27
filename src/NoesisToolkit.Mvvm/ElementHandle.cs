using System;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Mvvm;

/// <summary>A dependency object as Noesis holds it natively. Noesis keeps a native object's managed
/// proxy only weakly and mints a new one on the first touch after every collection; what reads, writes
/// and subscribes through the handle never has one minted.</summary>
/// <remarks>A handle keeps nothing alive: used after Noesis destroys its object, it reaches freed
/// memory. <c>default</c> names no object, and only <see cref="IsFrameworkElement"/> and
/// <see cref="Object"/> accept it.</remarks>
public readonly struct ElementHandle : IEquatable<ElementHandle>
{
    internal ElementHandle(nint pointer) => Pointer = pointer;

    internal nint Pointer { get; }

    /// <summary>Whether the object is a <see cref="FrameworkElement"/>, answered from its native type.</summary>
    public bool IsFrameworkElement =>
        Pointer != IntPtr.Zero && NoesisInternals.IsFrameworkElement(Pointer);

    /// <summary>The object's managed proxy, which Noesis mints when it holds none.</summary>
    public DependencyObject? Object =>
        Pointer == IntPtr.Zero
            ? null
            : NoesisInternals.Proxy(null, Pointer, false) as DependencyObject;

    /// <summary>The handle of <paramref name="target"/>.</summary>
    /// <param name="target">The object.</param>
    /// <returns>Its native handle.</returns>
    public static ElementHandle Of(DependencyObject target)
    {
        Guard.NotNull(target, nameof(target));
        return new ElementHandle(BaseComponent.getCPtr(target).Handle);
    }

    /// <inheritdoc />
    public bool Equals(ElementHandle other) => Pointer == other.Pointer;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ElementHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Pointer.GetHashCode();

    /// <summary>Whether both name the same native object.</summary>
    public static bool operator ==(ElementHandle left, ElementHandle right) => left.Equals(right);

    /// <summary>Whether they name different native objects.</summary>
    public static bool operator !=(ElementHandle left, ElementHandle right) => !left.Equals(right);
}

/// <summary>A change callback that takes its object by handle, so running it mints no proxy.</summary>
/// <param name="element">The object whose property changed.</param>
/// <param name="e">The change; valid for the callback's duration only.</param>
public delegate void ElementChangedCallback(
    ElementHandle element,
    DependencyPropertyChangedEventArgs e
);
