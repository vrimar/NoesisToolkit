using System;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>What a target slot shows while the binding in it holds nothing. Every compiled
/// binding kind needs this, so it belongs to none of them.</summary>
static class SlotDefault
{
    /// <summary>What the slot shows while the binding holds nothing: the default in force for this
    /// receiver, and whether a null one means the value must be cleared instead of written.</summary>
    internal static (object? Value, bool ClearWhenUnset) For(
        DependencyObject target,
        DependencyProperty property
    )
    {
        var unset = Unbox(
            property.GetMetadata(target.GetType())?.DefaultValue,
            property.PropertyType
        );
        return (unset, unset is null && property.PropertyType.IsValueType);
    }

    // Metadata hands an enum default back as the integer it is stored as, which is not a value the
    // property itself accepts.
    static object? Unbox(object? value, Type type) =>
        value is not null && type.IsEnum && !type.IsInstanceOfType(value)
            ? Enum.ToObject(type, value)
            : value;
}
