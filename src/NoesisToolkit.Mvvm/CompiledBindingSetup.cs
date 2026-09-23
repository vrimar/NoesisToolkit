using System;
using System.Collections.Generic;
using System.ComponentModel;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Wires a compiled binding inside a template. A local value set on the prototype is copied
/// to every clone, so an attached callback runs on each one — and the element keeps its own type,
/// which a generated subclass would not: an implicit style is keyed by the exact type.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CompiledBindingSetup
{
    static readonly List<Action<FrameworkElement>> Registered =
        new List<Action<FrameworkElement>>();

    // Slotted by key so a rebuild reuses its entry; hot reload runs every Build again.
    static readonly Dictionary<string, int> Slots = new Dictionary<string, int>(
        StringComparer.Ordinal
    );

    /// <summary>The index of the wiring to run.</summary>
    public static readonly DependencyProperty IndexProperty = DependencyProperty.RegisterAttached(
        "Index",
        typeof(int),
        typeof(CompiledBindingSetup),
        DependencyWatcher.Metadata(-1, FrameworkPropertyMetadataOptions.None, OnIndexChanged)
    );

    /// <summary>Records one element's wiring and returns the index that replays it on a clone.</summary>
    /// <param name="key">Identifies the element across rebuilds, so one slot is reused.</param>
    /// <param name="wire">Applied to each clone of the element this index is set on.</param>
    /// <returns>The index to hand <see cref="SetIndex"/>.</returns>
    public static int Register(string key, Action<FrameworkElement> wire)
    {
        Guard.NotNull(key, nameof(key));
        Guard.NotNull(wire, nameof(wire));

        // Generated static initialisers run these; two views wiring at once would interleave the
        // append and the slot it hands back.
        lock (Registered)
        {
            if (Slots.TryGetValue(key, out var slot))
            {
                Registered[slot] = wire;
                return slot;
            }

            Registered.Add(wire);
            Slots[key] = Registered.Count - 1;
            return Registered.Count - 1;
        }
    }

    /// <summary>Sets the wiring index on an element, which its clones inherit.</summary>
    /// <param name="target">The element to wire.</param>
    /// <param name="value">An index from <see cref="Register"/>.</param>
    public static void SetIndex(DependencyObject target, int value)
    {
        Guard.NotNull(target, nameof(target));
        DependencyWrite.Value(target, IndexProperty, value);
    }

    static void OnIndexChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        // e.NewValue boxes on every read.
        if (target is not FrameworkElement element)
            return;

        var index = DependencyRead.Int(target, IndexProperty);

        Action<FrameworkElement>? wire = null;
        lock (Registered)
        {
            if (index >= 0 && index < Registered.Count)
                wire = Registered[index];
        }

        wire?.Invoke(element);
    }
}
