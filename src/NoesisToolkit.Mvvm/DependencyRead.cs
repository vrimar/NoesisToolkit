using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Reads a dependency property without the box Noesis' <c>GetValue</c> makes for every
/// value type: a bool comes back as one of two shared boxes, and an int, float, double or enum as a
/// box kept per value. Anything else reads as Noesis reads it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DependencyRead
{
    static readonly Dictionary<DependencyProperty, Reader> Readers =
        new Dictionary<DependencyProperty, Reader>();

    /// <summary>Reads a bool property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static bool Bool(DependencyObject source, DependencyProperty property) =>
        Native.Bool(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads an int property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static int Int(DependencyObject source, DependencyProperty property) =>
        Native.Int(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads a float property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static float Float(DependencyObject source, DependencyProperty property) =>
        Native.Float(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads a double property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static double Double(DependencyObject source, DependencyProperty property) =>
        Native.Double(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads an enum property with no box at all.</summary>
    /// <typeparam name="TEnum">The enum type, stored by Noesis as its unsigned bits.</typeparam>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static TEnum Enum<TEnum>(DependencyObject source, DependencyProperty property)
        where TEnum : struct, System.Enum
    {
        var raw = Native.UInt64(null, Handle(source), Handle(property), false, out _);
        return Unsafe.SizeOf<TEnum>() switch
        {
            1 => Unsafe.BitCast<byte, TEnum>((byte)raw),
            2 => Unsafe.BitCast<ushort, TEnum>((ushort)raw),
            4 => Unsafe.BitCast<uint, TEnum>((uint)raw),
            _ => Unsafe.BitCast<ulong, TEnum>(raw),
        };
    }

    static nint Handle(BaseComponent component)
    {
        Guard.NotNull(component, nameof(component));
        return BaseComponent.getCPtr(component).Handle;
    }

    /// <summary>The value of <paramref name="property"/> on <paramref name="source"/>.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value, boxed once per distinct bool, int, float, double or enum value.</returns>
    public static object? Value(DependencyObject source, DependencyProperty property)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(property, nameof(property));

        if (!Readers.TryGetValue(property, out var reader))
        {
            reader = Reader.For(property);
            Readers[property] = reader;
        }

        return reader.Read(source, property);
    }

    abstract class Reader
    {
        internal static Reader For(DependencyProperty property)
        {
            var type = property.PropertyType;
            if (type == typeof(bool))
                return BoolReader.Instance;
            if (type == typeof(int))
                return new IntReader();
            if (type.IsEnum)
                return new EnumReader(type);
            if (type == typeof(float))
                return new FloatReader();
            if (type == typeof(double))
                return new DoubleReader();
            return PlainReader.Instance;
        }

        internal abstract object? Read(DependencyObject source, DependencyProperty property);
    }

    sealed class PlainReader : Reader
    {
        internal static readonly PlainReader Instance = new PlainReader();

        internal override object? Read(DependencyObject source, DependencyProperty property) =>
            source.GetValue(property);
    }

    sealed class BoolReader : Reader
    {
        internal static readonly BoolReader Instance = new BoolReader();

        static readonly object True = true;
        static readonly object False = false;

        internal override object Read(DependencyObject source, DependencyProperty property) =>
            Native.Bool(
                null,
                BaseComponent.getCPtr(source).Handle,
                BaseComponent.getCPtr(property).Handle,
                false,
                out _
            )
                ? True
                : False;
    }

    sealed class IntReader : Reader
    {
        // A counter shown in the UI lives in small values; a larger one boxes as Noesis would.
        readonly object?[] _small = new object?[256];

        internal override object Read(DependencyObject source, DependencyProperty property)
        {
            var raw = Native.Int(
                null,
                BaseComponent.getCPtr(source).Handle,
                BaseComponent.getCPtr(property).Handle,
                false,
                out _
            );

            return (uint)raw < (uint)_small.Length ? _small[raw] ??= raw : raw;
        }
    }

    sealed class EnumReader(Type type) : Reader
    {
        const int Cap = 64;

        readonly Dictionary<ulong, object> _boxes = new Dictionary<ulong, object>();

        internal override object Read(DependencyObject source, DependencyProperty property)
        {
            var raw = Native.UInt64(
                null,
                BaseComponent.getCPtr(source).Handle,
                BaseComponent.getCPtr(property).Handle,
                false,
                out _
            );

            if (_boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = System.Enum.ToObject(type, raw);
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    // A layout value repeats across instances, so the boxes are kept per value up to a cap.
    sealed class FloatReader : Reader
    {
        const int Cap = 64;

        readonly Dictionary<float, object> _boxes = new Dictionary<float, object>();

        internal override object Read(DependencyObject source, DependencyProperty property)
        {
            var raw = Native.Float(
                null,
                BaseComponent.getCPtr(source).Handle,
                BaseComponent.getCPtr(property).Handle,
                false,
                out _
            );

            if (_boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = raw;
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    sealed class DoubleReader : Reader
    {
        const int Cap = 64;

        readonly Dictionary<double, object> _boxes = new Dictionary<double, object>();

        internal override object Read(DependencyObject source, DependencyProperty property)
        {
            var raw = Native.Double(
                null,
                BaseComponent.getCPtr(source).Handle,
                BaseComponent.getCPtr(property).Handle,
                false,
                out _
            );

            if (_boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = raw;
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    // Noesis' own typed getters, which its GetValue boxes the result of on every read.
    static class Native
    {
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Float")]
        internal static extern float Float(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Double")]
        internal static extern double Double(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Bool")]
        internal static extern bool Bool(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Int")]
        internal static extern int Int(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_UInt64")]
        internal static extern ulong UInt64(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );
    }
}
