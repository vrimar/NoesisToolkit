using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Reads a dependency property without the box Noesis' <c>GetValue</c> makes for every
/// value type: a bool comes back as one of two shared boxes, and an int, long, float, double, enum,
/// <see cref="Noesis.Thickness"/>, <see cref="Noesis.CornerRadius"/>, <see cref="Noesis.Point"/>,
/// <see cref="Noesis.Size"/> or <see cref="Noesis.Color"/> as a box kept per value, nullable or not.
/// Text — a string property, or an object one holding a string — comes back as the string already
/// decoded for it, where Noesis decodes afresh. Anything else reads as Noesis reads it.</summary>
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

    /// <summary>Reads a <see cref="Noesis.Thickness"/> property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Thickness Thickness(
        DependencyObject source,
        DependencyProperty property
    ) => *(Thickness*)Native.Thickness(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.CornerRadius"/> property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe CornerRadius CornerRadius(
        DependencyObject source,
        DependencyProperty property
    ) => *(CornerRadius*)Native.CornerRadius(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.Point"/> property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Point Point(DependencyObject source, DependencyProperty property) =>
        *(Point*)Native.Point(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.Size"/> property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Size Size(DependencyObject source, DependencyProperty property) =>
        *(Size*)Native.Size(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.Color"/> property with no box at all.</summary>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Color Color(DependencyObject source, DependencyProperty property) =>
        *(Color*)Native.Color(null, Handle(source), Handle(property), false, out _);

    /// <summary>Reads an enum property with no box at all.</summary>
    /// <typeparam name="TEnum">The enum type, stored by Noesis as its unsigned bits.</typeparam>
    /// <param name="source">The object to read.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static TEnum Enum<TEnum>(DependencyObject source, DependencyProperty property)
        where TEnum : struct, System.Enum
    {
        return Bits<TEnum>(Native.UInt64(null, Handle(source), Handle(property), false, out _));
    }

    static TEnum Bits<TEnum>(ulong raw)
        where TEnum : struct, System.Enum =>
        Unsafe.SizeOf<TEnum>() switch
        {
            1 => Unsafe.BitCast<byte, TEnum>((byte)raw),
            2 => Unsafe.BitCast<ushort, TEnum>((ushort)raw),
            4 => Unsafe.BitCast<uint, TEnum>((uint)raw),
            _ => Unsafe.BitCast<ulong, TEnum>(raw),
        };

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

    internal static bool? TextEquals(nint source, DependencyProperty property, string text) =>
        ReaderOf(property) is StringReader
            ? NativeStrings.Matches(Native.String(null, source, Handle(property)), text)
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

        return ReaderOf(property).Read(Handle(source), property);
    }

    /// <summary>The value of <paramref name="property"/> on the object <paramref name="source"/> names.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value, boxed as <see cref="Value(DependencyObject, DependencyProperty)"/> boxes it.</returns>
    public static object? Value(ElementHandle source, DependencyProperty property)
    {
        Guard.NotNull(property, nameof(property));
        return Value(source.Pointer, property);
    }

    /// <summary>Reads a bool property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static bool Bool(ElementHandle source, DependencyProperty property) =>
        Bool(source.Pointer, property);

    /// <summary>Reads an int property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static int Int(ElementHandle source, DependencyProperty property) =>
        Int(source.Pointer, property);

    /// <summary>Reads a long property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static long Long(ElementHandle source, DependencyProperty property) =>
        Long(source.Pointer, property);

    /// <summary>Reads a float property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static float Float(ElementHandle source, DependencyProperty property) =>
        Float(source.Pointer, property);

    /// <summary>Reads a string property as the string already decoded for the same text.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value; empty for a null string, as Noesis returns it.</returns>
    public static string String(ElementHandle source, DependencyProperty property) =>
        NativeStrings.Decode(Native.String(null, source.Pointer, Handle(property)));

    /// <summary>Reads a double property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static double Double(ElementHandle source, DependencyProperty property) =>
        Double(source.Pointer, property);

    /// <summary>Reads an enum property with no box at all.</summary>
    /// <typeparam name="TEnum">The enum type, stored by Noesis as its unsigned bits.</typeparam>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static TEnum Enum<TEnum>(ElementHandle source, DependencyProperty property)
        where TEnum : struct, System.Enum =>
        Bits<TEnum>(Native.UInt64(null, source.Pointer, Handle(property), false, out _));

    /// <summary>Reads a <see cref="Noesis.Thickness"/> property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Thickness Thickness(ElementHandle source, DependencyProperty property) =>
        *(Thickness*)Native.Thickness(null, source.Pointer, Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.CornerRadius"/> property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe CornerRadius CornerRadius(
        ElementHandle source,
        DependencyProperty property
    ) => *(CornerRadius*)Native.CornerRadius(null, source.Pointer, Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.Point"/> property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Point Point(ElementHandle source, DependencyProperty property) =>
        *(Point*)Native.Point(null, source.Pointer, Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.Size"/> property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Size Size(ElementHandle source, DependencyProperty property) =>
        *(Size*)Native.Size(null, source.Pointer, Handle(property), false, out _);

    /// <summary>Reads a <see cref="Noesis.Color"/> property with no box at all.</summary>
    /// <param name="source">The object to read, by handle.</param>
    /// <param name="property">The property to read.</param>
    /// <returns>The value.</returns>
    public static unsafe Color Color(ElementHandle source, DependencyProperty property) =>
        *(Color*)Native.Color(null, source.Pointer, Handle(property), false, out _);

    internal static object? Value(nint source, DependencyProperty property) =>
        ReaderOf(property).Read(source, property);

    /// <summary>Makes a property of a type Noesis' <c>GetValue</c> reads as a pointer readable through
    /// <see cref="Value(DependencyObject, DependencyProperty)"/>: <paramref name="metadata"/> takes a
    /// coerce callback that hands the effective value over.</summary>
    /// <param name="metadata">The metadata to register the property with; its coerce callback is replaced.</param>
    /// <returns><paramref name="metadata"/>, so the call can wrap a registration's metadata.</returns>
    public static FrameworkPropertyMetadata Capturing(FrameworkPropertyMetadata metadata)
    {
        Guard.NotNull(metadata, nameof(metadata));
        metadata.CoerceValueCallback = Capture;
        return metadata;
    }

    static readonly CoerceValueCallback Capture = OnCoerce;

    static readonly object NotCaptured = new object();

    [ThreadStatic]
    static object? _captured;

    static object OnCoerce(DependencyObject target, object baseValue)
    {
        if (ReferenceEquals(_captured, NotCaptured))
            _captured = baseValue;

        return baseValue;
    }

    static object? Captured(DependencyObject source, DependencyProperty property)
    {
        _captured = NotCaptured;
        source.CoerceValue(property);
        var value = _captured;
        _captured = null;

        return ReferenceEquals(value, NotCaptured)
            ? throw new NotSupportedException(
                $"Noesis reads a {property.PropertyType.Name} property as a pointer: '{property.Name}' "
                    + "is readable from managed code only when registered through DependencyRead.Capturing."
            )
            : value;
    }

    internal static bool Bool(nint source, DependencyProperty property) =>
        Native.Bool(null, source, Handle(property), false, out _);

    internal static int Int(nint source, DependencyProperty property) =>
        Native.Int(null, source, Handle(property), false, out _);

    internal static long Long(nint source, DependencyProperty property) =>
        Native.Int64(null, source, Handle(property), false, out _);

    internal static float Float(nint source, DependencyProperty property) =>
        Native.Float(null, source, Handle(property), false, out _);

    internal static double Double(nint source, DependencyProperty property) =>
        Native.Double(null, source, Handle(property), false, out _);

    abstract class Reader
    {
        internal static Reader For(DependencyProperty property)
        {
            var type = property.PropertyType;
            if (type.IsEnum)
                return new EnumReader(type);

            // Noesis reads a nullable through the same typed native, asked whether it holds null.
            var value = Nullable.GetUnderlyingType(type) ?? type;
            var nullable = value != type;
            if (value == typeof(bool))
                return nullable ? BoolReader.OrNull : BoolReader.Instance;
            if (value == typeof(int))
                return new IntReader(nullable);
            if (value == typeof(long))
                return new LongReader(nullable);
            if (value == typeof(float))
                return new FloatReader(nullable);
            if (value == typeof(double))
                return new DoubleReader(nullable);
            if (value == typeof(Thickness))
                return new StructReader<Thickness>(
                    static (nint s, nint p, bool n, out bool isNull) =>
                        Native.Thickness(null, s, p, n, out isNull),
                    nullable
                );
            if (value == typeof(CornerRadius))
                return new StructReader<CornerRadius>(
                    static (nint s, nint p, bool n, out bool isNull) =>
                        Native.CornerRadius(null, s, p, n, out isNull),
                    nullable
                );
            if (value == typeof(Point))
                return new StructReader<Point>(
                    static (nint s, nint p, bool n, out bool isNull) =>
                        Native.Point(null, s, p, n, out isNull),
                    nullable
                );
            if (value == typeof(Size))
                return new StructReader<Size>(
                    static (nint s, nint p, bool n, out bool isNull) =>
                        Native.Size(null, s, p, n, out isNull),
                    nullable
                );
            if (value == typeof(Color))
                return new StructReader<Color>(
                    static (nint s, nint p, bool n, out bool isNull) =>
                        Native.Color(null, s, p, n, out isNull),
                    nullable
                );
            if (type == typeof(string))
                return StringReader.Instance;
            // Noesis reports its own object-typed properties as BaseComponent.
            if (type == typeof(object) || type == typeof(BaseComponent))
                return ObjectReader.Instance;
            if (NativeSlots.Misrouted(type))
                return CapturedReader.Instance;
            return NativeSlots.Typed(type) ? PlainReader.Instance : ComponentReader.Instance;
        }

        internal abstract object? Read(nint source, DependencyProperty property);
    }

    sealed class CapturedReader : Reader
    {
        internal static readonly CapturedReader Instance = new CapturedReader();

        internal override object? Read(nint source, DependencyProperty property) =>
            NoesisInternals.Proxy(null, source, false) is DependencyObject target
                ? Captured(target, property)
                : null;
    }

    // A struct or nullable Noesis marshals itself; only this read still goes through a proxy.
    sealed class PlainReader : Reader
    {
        internal static readonly PlainReader Instance = new PlainReader();

        internal override object? Read(nint source, DependencyProperty property) =>
            (NoesisInternals.Proxy(null, source, false) as DependencyObject)?.GetValue(property);
    }

    // What Noesis' GetValue does for any type its typed getters do not know, minus the source's proxy.
    sealed class ComponentReader : Reader
    {
        internal static readonly ComponentReader Instance = new ComponentReader();

        internal override object? Read(nint source, DependencyProperty property) =>
            NoesisInternals.Proxy(null, Native.Component(null, source, Handle(property)), false);
    }

    sealed class StringReader : Reader
    {
        internal static readonly StringReader Instance = new StringReader();

        internal override object Read(nint source, DependencyProperty property) =>
            NativeStrings.Decode(Native.String(null, source, Handle(property)));
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
        static readonly Dictionary<float, object> Floats = new Dictionary<float, object>(
            BitwiseFloat.Instance
        );
        static readonly Dictionary<double, object> Doubles = new Dictionary<double, object>(
            BitwiseDouble.Instance
        );

        internal override object? Read(nint source, DependencyProperty property)
        {
            var value = Native.Component(null, source, Handle(property));
            if (value == IntPtr.Zero)
                return null;

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

            return NoesisInternals.Proxy(null, value, false);
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

    sealed class BoolReader(bool nullable) : Reader
    {
        internal static readonly BoolReader Instance = new BoolReader(false);
        internal static readonly BoolReader OrNull = new BoolReader(true);

        static readonly object True = true;
        static readonly object False = false;

        internal override object? Read(nint source, DependencyProperty property)
        {
            var raw = Native.Bool(null, source, Handle(property), nullable, out var isNull);

            return nullable && isNull ? null
                : raw ? True
                : False;
        }
    }

    sealed class IntReader(bool nullable) : Reader
    {
        // A counter shown in the UI lives in small values; a larger one boxes as Noesis would.
        readonly object?[] _small = new object?[256];

        internal override object? Read(nint source, DependencyProperty property)
        {
            var raw = Native.Int(null, source, Handle(property), nullable, out var isNull);
            if (nullable && isNull)
                return null;

            return (uint)raw < (uint)_small.Length ? _small[raw] ??= raw : raw;
        }
    }

    sealed class LongReader(bool nullable) : Reader
    {
        const int Cap = 64;

        readonly Dictionary<long, object> _boxes = new Dictionary<long, object>();

        internal override object? Read(nint source, DependencyProperty property)
        {
            var raw = Native.Int64(null, source, Handle(property), nullable, out var isNull);
            if (nullable && isNull)
                return null;

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

        internal override object Read(nint source, DependencyProperty property)
        {
            var raw = Native.UInt64(null, source, Handle(property), false, out _);

            if (_boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = System.Enum.ToObject(type, raw);
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    // A layout value repeats across instances, so the boxes are kept per value up to a cap.
    sealed class FloatReader(bool nullable) : Reader
    {
        const int Cap = 64;

        readonly Dictionary<float, object> _boxes = new Dictionary<float, object>(
            BitwiseFloat.Instance
        );

        internal override object? Read(nint source, DependencyProperty property)
        {
            var raw = Native.Float(null, source, Handle(property), nullable, out var isNull);
            if (nullable && isNull)
                return null;

            if (_boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = raw;
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    sealed class DoubleReader(bool nullable) : Reader
    {
        const int Cap = 64;

        readonly Dictionary<double, object> _boxes = new Dictionary<double, object>(
            BitwiseDouble.Instance
        );

        internal override object? Read(nint source, DependencyProperty property)
        {
            var raw = Native.Double(null, source, Handle(property), nullable, out var isNull);
            if (nullable && isNull)
                return null;

            if (_boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = raw;
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    delegate nint StructRead(nint source, nint property, bool nullable, out bool isNull);

    // Noesis hands a struct back as a pointer to its value, which repeats across instances as a float does.
    sealed unsafe class StructReader<T>(StructRead read, bool nullable) : Reader
        where T : unmanaged
    {
        const int Cap = 64;

        readonly Dictionary<T, object> _boxes = new Dictionary<T, object>(
            BitwiseStruct<T>.Instance
        );

        internal override object? Read(nint source, DependencyProperty property)
        {
            var at = read(source, Handle(property), nullable, out var isNull);
            if (nullable && isNull)
                return null;

            var raw = *(T*)at;
            if (_boxes.TryGetValue(raw, out var boxed))
                return boxed;

            boxed = raw;
            if (_boxes.Count < Cap)
                _boxes[raw] = boxed;

            return boxed;
        }
    }

    // Equal is not identical for -0.0, so a box is shared only by the same bits.
    sealed class BitwiseFloat : IEqualityComparer<float>
    {
        internal static readonly BitwiseFloat Instance = new BitwiseFloat();

        public bool Equals(float x, float y) =>
            BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(y);

        public int GetHashCode(float value) => BitConverter.SingleToInt32Bits(value);
    }

    sealed class BitwiseDouble : IEqualityComparer<double>
    {
        internal static readonly BitwiseDouble Instance = new BitwiseDouble();

        public bool Equals(double x, double y) =>
            BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y);

        public int GetHashCode(double value) => BitConverter.DoubleToInt64Bits(value).GetHashCode();
    }

    sealed class BitwiseStruct<T> : IEqualityComparer<T>
        where T : unmanaged
    {
        internal static readonly BitwiseStruct<T> Instance = new BitwiseStruct<T>();

        public bool Equals(T x, T y) => Bytes(in x).SequenceEqual(Bytes(in y));

        public int GetHashCode(T value)
        {
            var hash = new HashCode();
            hash.AddBytes(Bytes(in value));
            return hash.ToHashCode();
        }

        static ReadOnlySpan<byte> Bytes(in T value) =>
            MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value));
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

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Thickness")]
        internal static extern nint Thickness(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(
            UnsafeAccessorKind.StaticMethod,
            Name = "Noesis_DependencyGet_CornerRadius"
        )]
        internal static extern nint CornerRadius(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Point")]
        internal static extern nint Point(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Size")]
        internal static extern nint Size(
            DependencyObject? owner,
            nint dependencyObject,
            nint dependencyProperty,
            bool isNullable,
            out bool isNull
        );

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Noesis_DependencyGet_Color")]
        internal static extern nint Color(
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
