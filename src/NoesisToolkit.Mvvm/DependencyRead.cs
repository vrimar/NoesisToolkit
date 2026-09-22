using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Reads a dependency property without the box Noesis' <c>GetValue</c> makes for every
/// value type: a bool comes back as one of two shared boxes, and an int, long, float, double or enum
/// as a box kept per value. Text — a string property, or an object one holding a string — comes back as the
/// string already decoded for it, where Noesis decodes afresh. Anything else reads as Noesis reads it.</summary>
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

    /// <summary>Reads a long property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static long Long(DependencyObject source, DependencyProperty property) =>
        Native.Int64(null, Handle(source), Handle(property), false, out _);

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

    /// <summary>Reads a string property as the string already decoded for the same text, where Noesis
    /// decodes a fresh one on every read.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value; empty for a null string, as Noesis returns it.</returns>
    public static string String(DependencyObject source, DependencyProperty property) =>
        NativeStrings.Decode(Native.String(null, Handle(source), Handle(property)));

    /// <summary>Copies a string property's text into <paramref name="destination"/> without decoding
    /// a string, for a reader that only parses or measures it.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read, of type string.</param>
    /// <param name="destination">Where the text lands.</param>
    /// <param name="written">The characters written; zero for a null string, as Noesis reads it.</param>
    /// <returns>False where the text does not fit in <paramref name="destination"/>.</returns>
    public static bool TryCopyString(
        DependencyObject source,
        DependencyProperty property,
        Span<char> destination,
        out int written
    ) =>
        NativeStrings.TryCopy(
            Native.String(null, Handle(source), Handle(property)),
            destination,
            out written
        );

    internal static bool? TextEquals(
        DependencyObject source,
        DependencyProperty property,
        string text
    ) =>
        ReaderOf(property) is StringReader
            ? NativeStrings.Matches(Native.String(null, Handle(source), Handle(property)), text)
            : null;

    static Reader ReaderOf(DependencyProperty property)
    {
        if (!Readers.TryGetValue(property, out var reader))
        {
            reader = Reader.For(property);
            Readers[property] = reader;
        }

        return reader;
    }

    static nint Handle(BaseComponent component)
    {
        Guard.NotNull(component, nameof(component));
        return BaseComponent.getCPtr(component).Handle;
    }

    /// <summary>The value of <paramref name="property"/> on <paramref name="source"/>.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value, boxed once per distinct bool, int, long, float, double or enum value.</returns>
    public static object? Value(DependencyObject source, DependencyProperty property)
    {
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(property, nameof(property));

        return ReaderOf(property).Read(source, property);
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
            if (type == typeof(long))
                return new LongReader();
            if (type.IsEnum)
                return new EnumReader(type);
            if (type == typeof(float))
                return new FloatReader();
            if (type == typeof(double))
                return new DoubleReader();
            if (type == typeof(string))
                return StringReader.Instance;
            // Noesis reports its own object-typed properties as BaseComponent.
            if (type == typeof(object) || type == typeof(BaseComponent))
                return ObjectReader.Instance;
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

    sealed class StringReader : Reader
    {
        internal static readonly StringReader Instance = new StringReader();

        internal override object Read(DependencyObject source, DependencyProperty property) =>
            String(source, property);
    }

    // An object slot holding text, a flag or a number holds a native box, unboxed afresh per read.
    sealed class ObjectReader : Reader
    {
        internal static readonly ObjectReader Instance = new ObjectReader();

        const int Cap = 64;

        static readonly nint BoxedString = Native.BoxedStringType(null);
        static readonly nint BoxedBool = Native.BoxedBoolType(null);
        static readonly nint BoxedInt = Native.BoxedIntType(null);
        static readonly nint BoxedFloat = Native.BoxedFloatType(null);
        static readonly nint BoxedDouble = Native.BoxedDoubleType(null);

        static readonly object True = true;
        static readonly object False = false;
        static readonly object?[] SmallInts = new object?[256];
        static readonly Dictionary<int, object> Ints = new Dictionary<int, object>();
        static readonly Dictionary<float, object> Floats = new Dictionary<float, object>();
        static readonly Dictionary<double, object> Doubles = new Dictionary<double, object>();

        internal override object? Read(DependencyObject source, DependencyProperty property)
        {
            var value = Native.Component(null, Handle(source), Handle(property));
            if (value == IntPtr.Zero)
                return source.GetValue(property);

            var type = Native.DynamicType(null, value);
            if (type == BoxedString)
                return NativeStrings.Decode(Native.UnboxString(null, value));
            if (type == BoxedBool)
                return Native.UnboxBool(null, value) ? True : False;
            if (type == BoxedInt)
                return Int(Native.UnboxInt(null, value));
            if (type == BoxedFloat)
                return Shared(Floats, Native.UnboxFloat(null, value));
            if (type == BoxedDouble)
                return Shared(Doubles, Native.UnboxDouble(null, value));

            return source.GetValue(property);
        }

        static object Int(int raw) =>
            (uint)raw < (uint)SmallInts.Length ? SmallInts[raw] ??= raw : Shared(Ints, raw);

        static object Shared<T>(Dictionary<T, object> boxes, T raw)
            where T : struct
        {
            if (boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = raw;
            if (boxes.Count < Cap)
                boxes[raw] = boxed;

            return boxed;
        }
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

    sealed class LongReader : Reader
    {
        const int Cap = 64;

        readonly Dictionary<long, object> _boxes = new Dictionary<long, object>();

        internal override object Read(DependencyObject source, DependencyProperty property)
        {
            var raw = Native.Int64(
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
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_String")]
        internal static extern nint String(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty
        );

        [UnsafeAccessor(
            UnsafeAccessorKind.StaticMethod,
            Name = "Noesis_DependencyGet_BaseComponent"
        )]
        internal static extern nint Component(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetDynamicType")]
        internal static extern nint DynamicType(BaseComponent? owner, nint cPtr);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Boxed_String_GetStaticType")]
        internal static extern nint BoxedStringType(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Boxed_Bool_GetStaticType")]
        internal static extern nint BoxedBoolType(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Boxed_Int_GetStaticType")]
        internal static extern nint BoxedIntType(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Boxed_Float_GetStaticType")]
        internal static extern nint BoxedFloatType(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Boxed_Double_GetStaticType")]
        internal static extern nint BoxedDoubleType(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Unbox_Bool")]
        internal static extern bool UnboxBool(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
            nint boxed
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Unbox_Int")]
        internal static extern int UnboxInt(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
            nint boxed
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Unbox_Float")]
        internal static extern float UnboxFloat(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
            nint boxed
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Unbox_Double")]
        internal static extern double UnboxDouble(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
            nint boxed
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Unbox_String")]
        internal static extern nint UnboxString(
            [UnsafeAccessorType("Noesis.NoesisGUI_PINVOKE, Noesis.GUI")] object? owner,
            nint boxed
        );

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

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Int64")]
        internal static extern long Int64(
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
