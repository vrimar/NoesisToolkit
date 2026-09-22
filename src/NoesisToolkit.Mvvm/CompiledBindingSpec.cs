using System;
using System.ComponentModel;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Everything the compiler resolved about one binding, handed to
/// <see cref="CompiledBinding.Bind(FrameworkElement, DependencyProperty, CompiledBindingSpec)"/>
/// as a whole rather than as a positional list.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CompiledBindingSpec
{
    /// <summary>Given the target, the element the path starts at; null starts it at the target.</summary>
    public Func<FrameworkElement, FrameworkElement?>? Source { get; set; }

    /// <summary>The property on the source the path reads through; null reads its DataContext.</summary>
    public DependencyProperty? SourceProperty { get; set; }

    /// <summary>The path, one hop per dotted segment past the root.</summary>
    public BindingHop[] Hops { get; set; } = Array.Empty<BindingHop>();

    /// <summary>Turns the value the path produced into what the target property stores. It has to
    /// return a value that property's own type accepts, including for null.</summary>
    public Func<object?, object?> Convert { get; set; } = static v => v;

    /// <summary>Writes the converted value into the target property, in place of
    /// <c>SetValue</c>. Set only where <c>SetValue</c> cannot carry the value: it routes an enum
    /// through a 64-bit slot the native side rejects, and drops the write without saying so.</summary>
    public Action<FrameworkElement, object?>? Assign { get; set; }

    /// <summary>Writes a value back to the last hop. Null means the path is not writable, which
    /// makes the binding one-way whatever <see cref="Mode"/> asks for.</summary>
    public Action<object, object?>? Write { get; set; }

    /// <summary>Takes the last hop, the conversion, the slot write and the write back typed. Where
    /// set, <see cref="Convert"/>, <see cref="Assign"/> and <see cref="Write"/> go unused.</summary>
    public BindingLane? Lane { get; set; }

    /// <summary>Runs on the value the path produced, and in reverse on a write back.</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>Passed to the converter.</summary>
    public object? ConverterParameter { get; set; }

    /// <summary>Passed to the converter as the type it is converting to.</summary>
    public Type? TargetType { get; set; }

    /// <summary>As the document wrote it. <see cref="BindingMode.Default"/> defers to the target
    /// property's own <c>BindsTwoWayByDefault</c>.</summary>
    public BindingMode Mode { get; set; } = BindingMode.Default;

    /// <summary>As the document wrote it. <see cref="UpdateSourceTrigger.Default"/> defers to the
    /// target property's own <c>DefaultUpdateSourceTrigger</c>.</summary>
    public UpdateSourceTrigger Trigger { get; set; } = UpdateSourceTrigger.Default;
}
