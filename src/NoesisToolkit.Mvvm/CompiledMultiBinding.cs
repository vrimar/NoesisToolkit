using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>One source chain of a compiled MultiBinding: where it roots and the hops it reads.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledBindingPart
{
    /// <summary>Given the target, the element the path starts at; null starts it at the target.</summary>
    public Func<FrameworkElement, FrameworkElement?>? Source { get; set; }

    /// <summary>The property on the source the path reads through; null reads its DataContext.</summary>
    public DependencyProperty? SourceProperty { get; set; }

    /// <summary>The path, one hop per dotted segment past the root.</summary>
    public BindingHop[] Hops { get; set; } = Array.Empty<BindingHop>();
}

/// <summary>Everything the compiler resolved about one MultiBinding, handed to
/// <see cref="CompiledMultiBinding.Bind"/> as a whole. A compiled MultiBinding is one-way
/// only.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledMultiBindingSpec
{
    /// <summary>The child bindings, in document order.</summary>
    public CompiledBindingPart[] Parts { get; set; } = Array.Empty<CompiledBindingPart>();

    /// <summary>Combines the part values. A part whose path ran out arrives as
    /// <c>DependencyProperty.UnsetValue</c>, exactly as the native engine hands it over.</summary>
    public IMultiValueConverter? Converter { get; set; }

    /// <summary>Passed to the converter.</summary>
    public object? ConverterParameter { get; set; }

    /// <summary>Passed to the converter as the type it is converting to.</summary>
    public Type? TargetType { get; set; }

    /// <summary>Combines the part values where no converter does, typically a StringFormat. Unlike
    /// the converter it never sees a broken part: any unset value fails the whole binding
    /// first.</summary>
    public Func<object?[], object?>? Format { get; set; }

    /// <summary>Turns the combined value into what the target property stores.</summary>
    public Func<object?, object?> Convert { get; set; } = static v => v;

    /// <summary>Writes the converted value into the target property, in place of
    /// <c>SetValue</c>, where <c>SetValue</c> cannot carry the value.</summary>
    public Action<FrameworkElement, object?>? Assign { get; set; }
}

/// <summary>A MultiBinding the compiler resolved into typed chains, watched the same way a
/// <see cref="CompiledBinding"/> watches its one chain.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledMultiBinding
{
    readonly FrameworkElement _target;
    readonly DependencyProperty _property;
    readonly CompiledMultiBindingSpec _spec;
    readonly PartSet _parts;

    readonly object? _unset;
    readonly bool _clearWhenUnset;
    bool _pushing;

    CompiledMultiBinding(
        FrameworkElement target,
        DependencyProperty property,
        CompiledMultiBindingSpec spec
    )
    {
        _target = target;
        _property = property;
        _spec = spec;
        _parts = new PartSet(target, spec.Parts, Rebuild);

        (_unset, _clearWhenUnset) = SlotDefault.For(target, property);

        _parts.Start();
    }

    /// <summary>Binds <paramref name="property"/> on <paramref name="target"/> as
    /// <paramref name="spec"/> describes.</summary>
    /// <param name="target">The element to write.</param>
    /// <param name="property">The dependency property to write.</param>
    /// <param name="spec">What the compiler resolved about the MultiBinding.</param>
    /// <returns>The live binding, which follows the element for as long as it lives.</returns>
    public static CompiledMultiBinding Bind(
        FrameworkElement target,
        DependencyProperty property,
        CompiledMultiBindingSpec spec
    )
    {
        Guard.NotNull(target, nameof(target));
        Guard.NotNull(property, nameof(property));
        Guard.NotNull(spec, nameof(spec));

        return new CompiledMultiBinding(target, property, spec);
    }

    void Rebuild()
    {
        if (_pushing)
            return;

        _parts.Unwatch();

        var values = new object?[_parts.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = _parts.Evaluate(i);

        object? combined;
        if (_spec.Converter is not null)
            combined = _spec.Converter.Convert(
                values,
                _spec.TargetType ?? typeof(object),
                _spec.ConverterParameter,
                CultureInfo.CurrentCulture
            );
        else if (Array.IndexOf(values, DependencyProperty.UnsetValue) >= 0 || _spec.Format is null)
            combined = DependencyProperty.UnsetValue;
        else
            combined = _spec.Format(values);

        _pushing = true;
        try
        {
            // A failed binding still occupies the slot, so the metadata default is what shows.
            if (!ReferenceEquals(combined, DependencyProperty.UnsetValue))
                Assign(_spec.Convert(combined));
            else if (_clearWhenUnset)
                _target.ClearValue(_property);
            else
                Assign(_unset);
        }
        finally
        {
            _pushing = false;
        }
    }

    void Assign(object? value)
    {
        if (_spec.Assign is null)
            _target.SetValue(_property, value);
        else
            _spec.Assign(_target, value);
    }
}
