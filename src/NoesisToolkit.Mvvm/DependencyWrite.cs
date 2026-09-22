using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Writes a dependency property without the box Noesis' <c>SetValue</c> takes for every
/// value type, through the typed natives its own setters use.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DependencyWrite
{
    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(DependencyObject target, DependencyProperty property, bool value) =>
        Native.Bool(null, Handle(target), Handle(property), value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(DependencyObject target, DependencyProperty property, int value) =>
        Native.Int(null, Handle(target), Handle(property), value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(DependencyObject target, DependencyProperty property, float value) =>
        Native.Float(null, Handle(target), Handle(property), value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(DependencyObject target, DependencyProperty property, double value) =>
        Native.Double(null, Handle(target), Handle(property), value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(
        DependencyObject target,
        DependencyProperty property,
        Thickness value
    ) => Native.Thickness(null, Handle(target), Handle(property), ref value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(DependencyObject target, DependencyProperty property, Color value) =>
        Native.Color(null, Handle(target), Handle(property), ref value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(DependencyObject target, DependencyProperty property, Point value) =>
        Native.Point(null, Handle(target), Handle(property), ref value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(DependencyObject target, DependencyProperty property, Size value) =>
        Native.Size(null, Handle(target), Handle(property), ref value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    public static void Value(
        DependencyObject target,
        DependencyProperty property,
        CornerRadius value
    ) => Native.CornerRadius(null, Handle(target), Handle(property), ref value, false, false);

    /// <summary>Writes <paramref name="value"/> to <paramref name="property"/> on <paramref name="target"/>.</summary>
    /// <typeparam name="TEnum">The enum type, stored by Noesis as its unsigned bits.</typeparam>
    public static void Value<TEnum>(
        DependencyObject target,
        DependencyProperty property,
        TEnum value
    )
        where TEnum : struct, Enum
    {
        var raw = Unsafe.SizeOf<TEnum>() switch
        {
            1 => Unsafe.As<TEnum, byte>(ref value),
            2 => Unsafe.As<TEnum, ushort>(ref value),
            4 => Unsafe.As<TEnum, uint>(ref value),
            _ => Unsafe.As<TEnum, ulong>(ref value),
        };
        Native.UInt64(null, Handle(target), Handle(property), raw, false, false);
    }

    static nint Handle(BaseComponent component)
    {
        Guard.NotNull(component, nameof(component));
        return BaseComponent.getCPtr(component).Handle;
    }

    static class Native
    {
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Bool")]
        internal static extern void Bool(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Int")]
        internal static extern void Int(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            int val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Float")]
        internal static extern void Float(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            float val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Double")]
        internal static extern void Double(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            double val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_UInt64")]
        internal static extern void UInt64(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            ulong val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Thickness")]
        internal static extern void Thickness(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            ref Thickness val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Color")]
        internal static extern void Color(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            ref Color val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Point")]
        internal static extern void Point(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            ref Point val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Size")]
        internal static extern void Size(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            ref Size val,
            bool isNullable,
            bool isNull
        );

        [UnsafeAccessor(
            UnsafeAccessorKind.StaticMethod,
            Name = "Noesis_DependencySet_CornerRadius"
        )]
        internal static extern void CornerRadius(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            ref CornerRadius val,
            bool isNullable,
            bool isNull
        );
    }
}
