using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Mvvm;

/// <summary>Starts a drag without the objects Noesis' managed layer mints as one ends: its own
/// <see cref="DragDrop.DoDragDrop"/> wraps the data in a new <see cref="DataObject"/> and boxes the drop
/// point for every completion, which a drag-and-drop UI pays on every gesture. The completion here reads no
/// element, so it mints no proxy either.</summary>
public static class NativeDrag
{
    /// <summary>Called once as the drag ends, on the thread that started it.</summary>
    public delegate void Completed(DragDropEffects effects, Point dropPoint);

    static readonly Dictionary<int, Completed> Pending = new Dictionary<int, Completed>();
    static int _next;

    /// <summary>The same as <see cref="DragDrop.DoDragDrop"/>, without the completion's allocations. A
    /// <paramref name="completed"/> held in a static field keeps the whole gesture allocation-free.</summary>
    public static unsafe void DoDragDrop(
        DependencyObject source,
        object data,
        DragDropEffects allowedEffects,
        Completed completed
    )
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(data, nameof(data));
        Guard.NotNull(completed, nameof(completed));

        var id = _next++;
        Pending[id] = completed;
        DragDrop_DoDragDrop(
            NoesisInternals.InstanceHandle(null, source),
            NoesisInternals.InstanceHandle(null, data),
            (int)allowedEffects,
            id,
            &OnCompleted
        );
    }

    [UnmanagedCallersOnly]
    static unsafe void OnCompleted(
        int id,
        nint source,
        nint data,
        nint target,
        nint dropPoint,
        int effects
    )
    {
        try
        {
            if (Pending.Remove(id, out var completed))
                completed((DragDropEffects)effects, *(Point*)dropPoint);
        }
        catch (Exception exception)
        {
            Error.UnhandledException(exception);
        }
    }

    [DllImport("Noesis")]
    static extern unsafe void DragDrop_DoDragDrop(
        HandleRef source,
        HandleRef data,
        int allowedEffects,
        int callbackId,
        delegate* unmanaged<int, nint, nint, nint, nint, int, void> callback
    );
}
