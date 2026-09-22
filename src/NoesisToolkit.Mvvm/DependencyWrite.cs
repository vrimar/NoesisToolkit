using System;
using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    public static void Value(DependencyObject target, DependencyProperty property, long value) =>
        Native.Int64(null, Handle(target), Handle(property), value, false, false);

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

    /// <summary>Writes <paramref name="text"/> to a string property on <paramref name="target"/> without
    /// a managed string: the characters go to Noesis as they are, so text formatted into a buffer never
    /// becomes garbage.</summary>
    /// <param name="target">The object to write.</param>
    /// <param name="property">The property to write, of type string.</param>
    /// <param name="text">The text; empty writes the empty string, as Noesis stores a null one.</param>
    public static unsafe void String(
        DependencyObject target,
        DependencyProperty property,
        ReadOnlySpan<char> text
    )
    {
        var dependencyObject = Handle(target);
        var dependencyProperty = Handle(property);

        char[]? rented = null;
        var buffer =
            text.Length < StackChars
                ? stackalloc char[StackChars]
                : (rented = ArrayPool<char>.Shared.Rent(text.Length + 1));

        // Noesis reads the text up to its terminator.
        text.CopyTo(buffer);
        buffer[text.Length] = '\0';
        fixed (char* chars = buffer)
            Native.String(dependencyObject, dependencyProperty, chars);

        if (rented is not null)
            ArrayPool<char>.Shared.Return(rented);
    }

    const int StackChars = 256;

    static nint Handle(BaseComponent component)
    {
        Guard.NotNull(component, nameof(component));
        return BaseComponent.getCPtr(component).Handle;
    }

    static class Native
    {
        // Noesis' own setter passes a UTF-16 copy here; a ref char would marshal as one ANSI byte.
        [DllImport("Noesis", EntryPoint = "Noesis_DependencySet_String")]
        internal static extern unsafe void String(
            nint dependencyObject,
            nint dependencyProperty,
            char* val
        );

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

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencySet_Int64")]
        internal static extern void Int64(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            long val,
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
