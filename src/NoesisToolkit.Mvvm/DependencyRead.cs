using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Reads a dependency property without the box Noesis' <c>GetValue</c> makes for every
/// value type: a bool comes back as one of two shared boxes and an enum as a box kept per value.
/// Anything else reads as Noesis reads it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class DependencyRead
{
    static readonly Dictionary<DependencyProperty, Reader> Readers =
        new Dictionary<DependencyProperty, Reader>();

    /// <summary>The value of <paramref name="property"/> on <paramref name="source"/>.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value, boxed once per distinct bool or enum value.</returns>
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
#if NET8_0_OR_GREATER
            var type = property.PropertyType;
            if (type == typeof(bool))
                return BoolReader.Instance;
            if (type == typeof(int))
                return new IntReader();
            if (type.IsEnum)
                return new EnumReader(type);
#endif
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

#if NET8_0_OR_GREATER
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

            boxed = Enum.ToObject(type, raw);
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    // Noesis' own typed getters, which its GetValue boxes the result of on every read.
    static class Native
    {
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
#endif
}
