using System;
using System.Collections.Generic;
using System.ComponentModel;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>One condition of a compiled trigger: a source chain and the constant it must
/// equal.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledTriggerCondition
{
    /// <summary>Where the condition reads, shaped exactly like a MultiBinding part.</summary>
    public CompiledBindingPart Part { get; set; } = new CompiledBindingPart();

    /// <summary>The constant the chain value must equal, already converted to the chain's
    /// type.</summary>
    public object? Value { get; set; }
}

/// <summary>One setter a compiled trigger applies while its conditions hold.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledSetter
{
    /// <summary>The property the setter writes.</summary>
    public DependencyProperty Property { get; set; } = null!;

    /// <summary>The value, already converted to the property's type.</summary>
    public object? Value { get; set; }

    /// <summary>Writes the value in place of <c>SetValue</c>, where <c>SetValue</c> cannot carry
    /// it.</summary>
    public Action<FrameworkElement, object?>? Assign { get; set; }

    /// <summary>The template name the setter writes through; null writes the bound element
    /// itself.</summary>
    public string? TargetName { get; set; }
}

/// <summary>One DataTrigger or MultiDataTrigger the compiler resolved whole.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledTriggerSpec
{
    /// <summary>Every condition must hold; a chain that ran out holds nothing.</summary>
    public CompiledTriggerCondition[] Conditions { get; set; } =
        Array.Empty<CompiledTriggerCondition>();

    /// <summary>Applied while the conditions hold, cleared when they stop.</summary>
    public CompiledSetter[] Setters { get; set; } = Array.Empty<CompiledSetter>();

    /// <summary>True where the document declared the trigger on a template rather than in an
    /// element's own style.</summary>
    public bool TemplateScoped { get; set; }

    /// <summary>True where the conditions read dependency properties rather than data paths — a
    /// Trigger or MultiTrigger rather than a DataTrigger. A path off a named element has the same
    /// shape, so which one the document wrote cannot be read back off the conditions.</summary>
    public bool PropertyDriven { get; set; }
}

/// <summary>A style's compiled triggers, evaluated together because they share precedence: a later
/// trigger's setter beats an earlier one's on the same property, and a property no active trigger
/// sets falls back to whatever the style's ordinary setters give it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledTriggerSet : IPartSetOwner
{
    /// <summary>The trigger sets bound to <paramref name="element"/>, for tooling that compares the
    /// compiled graph against the parsed one.</summary>
    /// <param name="element">The element the sets were bound to.</param>
    /// <returns>The live sets, in binding order; empty when none were bound.</returns>
    public static IReadOnlyList<CompiledTriggerSet> SetsOf(FrameworkElement element)
    {
        Guard.NotNull(element, nameof(element));
        return ElementState.Find(BaseComponent.getCPtr(element).Handle)?.TriggerSets is { } sets
            ? sets
            : Array.Empty<CompiledTriggerSet>();
    }

    /// <summary>What the compiler resolved, in document order.</summary>
    public IReadOnlyList<CompiledTriggerSpec> Specs => _triggers;

    readonly ElementState _target;
    readonly CompiledTriggerSpec[] _triggers;
    readonly PartSet _parts;
    readonly Layout _layout;

    // One bit per setter slot, so a trigger holding or letting go writes the difference and nothing else.
    readonly ulong[] _applied;

    bool _pushing;
    bool _moved;

    // A clone's name scope can fill after the first pass, so keep retrying rather than drop it.
    bool _targetMissing;

    // A template's setters in document order, shared by every element the template is cloned onto.
    sealed class Layout
    {
        internal readonly CompiledSetter[] Slots;

        // Slots that write the same property through the same name share a key; the later slot wins.
        internal readonly int[] Keys;
        internal readonly int KeyCount;

        internal Layout(CompiledTriggerSpec[] triggers)
        {
            var slots = new List<CompiledSetter>();
            var keys = new List<int>();
            var ids = new Dictionary<(string?, DependencyProperty), int>();
            foreach (var trigger in triggers)
            {
                foreach (var setter in trigger.Setters)
                {
                    var key = (setter.TargetName, setter.Property);
                    if (!ids.TryGetValue(key, out var id))
                        ids[key] = id = ids.Count;

                    slots.Add(setter);
                    keys.Add(id);
                }
            }

            Slots = slots.ToArray();
            Keys = keys.ToArray();
            KeyCount = ids.Count;
        }

        internal static int Words(int bits) => (bits + 63) >> 6;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        CompiledTriggerSpec[],
        Layout
    > _layouts = new System.Runtime.CompilerServices.ConditionalWeakTable<
        CompiledTriggerSpec[],
        Layout
    >();

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        CompiledTriggerSpec[],
        Layout
    >.CreateValueCallback CreateLayout = static triggers => new Layout(triggers);

    CompiledTriggerSet(FrameworkElement target, CompiledTriggerSpec[] triggers)
    {
        _target = ElementState.Of(target);
        _triggers = triggers;
        _layout = _layouts.GetValue(triggers, CreateLayout);
        _applied = new ulong[Layout.Words(_layout.Slots.Length)];

        var count = 0;
        foreach (var trigger in triggers)
            count += trigger.Conditions.Length;

        var parts = new CompiledBindingPart[count];
        var next = 0;
        foreach (var trigger in triggers)
        {
            foreach (var condition in trigger.Conditions)
                parts[next++] = condition.Part;
        }

        _parts = new PartSet(_target, parts, this);

        var bound = _target.TriggerSets ??= new List<CompiledTriggerSet>();
        bound.RemoveAll(set => ReferenceEquals(set._triggers, triggers));
        bound.Add(this);

        _parts.Start();
    }

    /// <summary>Evaluates <paramref name="triggers"/> against <paramref name="target"/> for as long
    /// as the element lives.</summary>
    /// <param name="target">The element the triggers' setters write.</param>
    /// <param name="triggers">The compiled triggers, in document order.</param>
    /// <returns>The live set, which follows the element for as long as it lives.</returns>
    public static CompiledTriggerSet Bind(FrameworkElement target, CompiledTriggerSpec[] triggers)
    {
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(triggers, nameof(triggers));

        return new CompiledTriggerSet(target, triggers);
    }

    void IPartSetOwner.PartsChanged() => Rebuild();

    bool IPartSetOwner.StillMissing => _targetMissing;

    void Rebuild()
    {
        // A setter can write a property one of these conditions watches, which re-enters here.
        if (_pushing)
        {
            _moved = true;
            return;
        }

        for (var pass = 0; pass <= _triggers.Length; pass++)
        {
            _moved = false;
            Apply();
            if (!_moved)
                return;
        }
    }

    // A setter's write can reach a nested pass, so evaluation stays under the guard too.
    void Apply()
    {
        if (!_target.Alive)
            return;

        var target = _target.Handle;

        _pushing = true;
        try
        {
            _parts.Unwatch();

            var slots = _layout.Slots;
            var keys = _layout.Keys;
            var words = _applied.Length;

            Span<ulong> active = stackalloc ulong[words];
            var missing = false;
            var index = 0;
            var slot = 0;
            foreach (var trigger in _triggers)
            {
                var holds = true;
                foreach (var condition in trigger.Conditions)
                {
                    if (!_parts.Matches(index++, condition.Value))
                        holds = false;
                }

                foreach (var setter in trigger.Setters)
                {
                    if (holds)
                    {
                        if (SetterTarget(target, setter) == IntPtr.Zero)
                            missing = true;
                        else
                            Set(active, slot);
                    }

                    slot++;
                }
            }

            _targetMissing = missing;

            if (!Any(active) && !Any(_applied))
                return;

            Span<ulong> winners = stackalloc ulong[words];
            Span<ulong> keysWon = stackalloc ulong[Layout.Words(_layout.KeyCount)];
            for (var i = slots.Length - 1; i >= 0; i--)
            {
                if (Has(active, i) && !Has(keysWon, keys[i]))
                {
                    Set(winners, i);
                    Set(keysWon, keys[i]);
                }
            }

            Span<ulong> previous = stackalloc ulong[words];
            _applied.CopyTo(previous);
            winners.CopyTo(_applied);

            for (var i = 0; i < slots.Length; i++)
            {
                if (
                    Has(previous, i)
                    && !Has(winners, i)
                    && !Has(keysWon, keys[i])
                    && SetterTarget(target, slots[i]) is var cleared
                    && cleared != IntPtr.Zero
                )
                    DependencyWrite.Clear(cleared, slots[i].Property);
            }

            for (var i = 0; i < slots.Length; i++)
            {
                if (
                    !Has(winners, i)
                    || Has(previous, i)
                    || SetterTarget(target, slots[i]) is var element && element == IntPtr.Zero
                )
                    continue;

                var setter = slots[i];
                if (setter.Assign is null)
                    DependencyWrite.Value(element, setter.Property, setter.Value);
                else if (NoesisInternals.Proxy(null, element, false) is FrameworkElement assigned)
                    setter.Assign(assigned, setter.Value);
            }
        }
        finally
        {
            _pushing = false;
        }
    }

    static bool Has(ReadOnlySpan<ulong> bits, int i) => (bits[i >> 6] & (1UL << (i & 63))) != 0;

    static void Set(Span<ulong> bits, int i) => bits[i >> 6] |= 1UL << (i & 63);

    static bool Any(ReadOnlySpan<ulong> bits)
    {
        foreach (var word in bits)
        {
            if (word != 0)
                return true;
        }

        return false;
    }

    static nint SetterTarget(nint target, CompiledSetter setter) =>
        setter.TargetName is null ? target : NoesisInternals.FindElement(target, setter.TargetName);
}
