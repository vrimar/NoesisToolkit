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
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        FrameworkElement,
        List<CompiledTriggerSet>
    > _byElement = new System.Runtime.CompilerServices.ConditionalWeakTable<
        FrameworkElement,
        List<CompiledTriggerSet>
    >();

    /// <summary>The trigger sets bound to <paramref name="element"/>, for tooling that compares the
    /// compiled graph against the parsed one.</summary>
    /// <param name="element">The element the sets were bound to.</param>
    /// <returns>The live sets, in binding order; empty when none were bound.</returns>
    public static IReadOnlyList<CompiledTriggerSet> SetsOf(FrameworkElement element)
    {
        Guard.NotNull(element, nameof(element));
        return _byElement.TryGetValue(element, out var sets)
            ? sets
            : (IReadOnlyList<CompiledTriggerSet>)Array.Empty<CompiledTriggerSet>();
    }

    /// <summary>What the compiler resolved, in document order.</summary>
    public IReadOnlyList<CompiledTriggerSpec> Specs => _triggers;

    readonly FrameworkElement _target;
    readonly CompiledTriggerSpec[] _triggers;
    readonly PartSet _parts;

    // Both maps exist only once a trigger has held: most sets never do.
    Dictionary<(FrameworkElement, DependencyProperty), CompiledSetter>? _applied;
    Dictionary<(FrameworkElement, DependencyProperty), CompiledSetter>? _spare;

    bool _pushing;
    bool _moved;

    // A clone's name scope can fill after the first pass, so keep retrying rather than drop it.
    bool _targetMissing;

    CompiledTriggerSet(FrameworkElement target, CompiledTriggerSpec[] triggers)
    {
        _target = target;
        _triggers = triggers;

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

        _parts = new PartSet(target, parts, this);

        var bound = _byElement.GetOrCreateValue(target);
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

    // The two maps are shared with a nested pass, so evaluation stays under the guard too.
    void Apply()
    {
        _pushing = true;
        try
        {
            _parts.Unwatch();

            var active = _spare;
            active?.Clear();
            var missing = false;
            var index = 0;
            foreach (var trigger in _triggers)
            {
                var holds = true;
                foreach (var condition in trigger.Conditions)
                {
                    if (!Equals(_parts.Evaluate(index++), condition.Value))
                        holds = false;
                }

                if (!holds)
                    continue;

                foreach (var setter in trigger.Setters)
                {
                    if (SetterTarget(setter) is { } element)
                    {
                        active ??=
                            new Dictionary<
                                (FrameworkElement, DependencyProperty),
                                CompiledSetter
                            >();
                        active[(element, setter.Property)] = setter;
                    }
                    else
                    {
                        missing = true;
                    }
                }
            }

            _targetMissing = missing;

            var applied = _applied;
            if (applied is null && active is null)
                return;

            applied ??= new Dictionary<(FrameworkElement, DependencyProperty), CompiledSetter>();
            active ??= new Dictionary<(FrameworkElement, DependencyProperty), CompiledSetter>();
            _applied = active;
            _spare = applied;

            foreach (var pair in applied)
            {
                if (!active.ContainsKey(pair.Key))
                    pair.Key.Item1.ClearValue(pair.Key.Item2);
            }

            foreach (var pair in active)
            {
                if (
                    applied.TryGetValue(pair.Key, out var previous)
                    && ReferenceEquals(previous, pair.Value)
                )
                    continue;

                if (pair.Value.Assign is null)
                    pair.Key.Item1.SetValue(pair.Key.Item2, pair.Value.Value);
                else
                    pair.Value.Assign(pair.Key.Item1, pair.Value.Value);
            }
        }
        finally
        {
            _pushing = false;
        }
    }

    FrameworkElement? SetterTarget(CompiledSetter setter) =>
        setter.TargetName is null
            ? _target
            : _target.FindName(setter.TargetName) as FrameworkElement;
}
