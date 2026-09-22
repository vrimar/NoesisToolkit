using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The typed route of a compiled binding whose path ends in a flag, number or enum and
/// whose slot holds a flag or number, with no converter or format between them. The value is read
/// off the last hop's owner, converted and written to the slot, and a two-way change read back and
/// written to the source, all typed: nothing is boxed, and a slider dragged through values it never
/// showed before costs nothing, where the boxed route keeps a box per value.</summary>
/// <remarks>One lane serves every clone of a template, so it holds no state of its own; a binding
/// keeps what it last wrote as the slot value's bits.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class BindingLane
{
    private protected BindingLane() { }

    // False where a guarded owner is not the lane's type, which breaks the path.
    internal abstract bool TryRead(object owner, out ulong slot);

    internal abstract void Write(
        DependencyObject receiver,
        DependencyProperty property,
        ulong slot
    );

    internal abstract ulong Read(DependencyObject target, DependencyProperty property);

    internal abstract bool Same(ulong a, ulong b);

    internal abstract bool WritesBack { get; }

    internal abstract void WriteBack(object owner, ulong slot);

    /// <summary>A lane off a last hop of type <typeparamref name="TSource"/> into a slot of type
    /// <typeparamref name="TSlot"/>.</summary>
    /// <typeparam name="TOwner">The type the last hop reads off.</typeparam>
    /// <typeparam name="TSource">The last hop's type: a flag, number or enum.</typeparam>
    /// <typeparam name="TSlot">The slot's type: bool, int, long, float or double.</typeparam>
    /// <param name="read">Reads the last hop.</param>
    /// <param name="convert">Turns the source value into the slot's.</param>
    /// <param name="writeBack">Writes a slot value back to the last hop; null where it has no setter.</param>
    /// <param name="guarded">Whether the owner may turn out not to be a <typeparamref name="TOwner"/>,
    /// which breaks the path rather than throwing.</param>
    /// <returns>The lane.</returns>
    public static BindingLane Of<TOwner, TSource, TSlot>(
        Func<TOwner, TSource> read,
        Func<TSource, TSlot> convert,
        Action<TOwner, TSlot>? writeBack,
        bool guarded
    )
        where TSource : struct
        where TSlot : struct
    {
        Guard.NotNull(read, nameof(read));
        Guard.NotNull(convert, nameof(convert));
        if (!Slots<TSlot>.Typed)
            throw new NotSupportedException(
                $"A lane writes a bool, int, long, float or double slot, not {typeof(TSlot)}."
            );

        return new Lane<TOwner, TSource, TSlot>(read, convert, writeBack, guarded);
    }

    sealed class Lane<TOwner, TSource, TSlot>(
        Func<TOwner, TSource> read,
        Func<TSource, TSlot> convert,
        Action<TOwner, TSlot>? writeBack,
        bool guarded
    ) : BindingLane
        where TSource : struct
        where TSlot : struct
    {
        internal override bool TryRead(object owner, out ulong slot)
        {
            TOwner typed;
            if (guarded)
            {
                if (owner is not TOwner matched)
                {
                    slot = 0;
                    return false;
                }

                typed = matched;
            }
            else
            {
                typed = (TOwner)owner;
            }

            slot = Slots<TSlot>.Bits(convert(read(typed)));
            return true;
        }

        internal override void Write(
            DependencyObject receiver,
            DependencyProperty property,
            ulong slot
        ) => Slots<TSlot>.Write(receiver, property, Slots<TSlot>.Value(slot));

        internal override ulong Read(DependencyObject target, DependencyProperty property) =>
            Slots<TSlot>.Bits(Slots<TSlot>.Read(target, property));

        internal override bool Same(ulong a, ulong b) =>
            EqualityComparer<TSlot>.Default.Equals(Slots<TSlot>.Value(a), Slots<TSlot>.Value(b));

        internal override bool WritesBack => writeBack is not null;

        internal override void WriteBack(object owner, ulong slot) =>
            writeBack!((TOwner)owner, Slots<TSlot>.Value(slot));
    }

    // Each branch is a constant for the one TSlot a lane is built over, so only its own survives.
    static class Slots<TSlot>
        where TSlot : struct
    {
        internal static readonly bool Typed =
            typeof(TSlot) == typeof(bool)
            || typeof(TSlot) == typeof(int)
            || typeof(TSlot) == typeof(long)
            || typeof(TSlot) == typeof(float)
            || typeof(TSlot) == typeof(double);

        internal static ulong Bits(TSlot value)
        {
            ulong bits = 0;
            Unsafe.As<ulong, TSlot>(ref bits) = value;
            return bits;
        }

        internal static TSlot Value(ulong bits) => Unsafe.As<ulong, TSlot>(ref bits);

        internal static TSlot Read(DependencyObject target, DependencyProperty property)
        {
            if (typeof(TSlot) == typeof(bool))
                return As(DependencyRead.Bool(target, property));
            if (typeof(TSlot) == typeof(int))
                return As(DependencyRead.Int(target, property));
            if (typeof(TSlot) == typeof(long))
                return As(DependencyRead.Long(target, property));
            if (typeof(TSlot) == typeof(float))
                return As(DependencyRead.Float(target, property));
            return As(DependencyRead.Double(target, property));
        }

        internal static void Write(
            DependencyObject target,
            DependencyProperty property,
            TSlot value
        )
        {
            if (typeof(TSlot) == typeof(bool))
                DependencyWrite.Value(target, property, Unsafe.As<TSlot, bool>(ref value));
            else if (typeof(TSlot) == typeof(int))
                DependencyWrite.Value(target, property, Unsafe.As<TSlot, int>(ref value));
            else if (typeof(TSlot) == typeof(long))
                DependencyWrite.Value(target, property, Unsafe.As<TSlot, long>(ref value));
            else if (typeof(TSlot) == typeof(float))
                DependencyWrite.Value(target, property, Unsafe.As<TSlot, float>(ref value));
            else
                DependencyWrite.Value(target, property, Unsafe.As<TSlot, double>(ref value));
        }

        static TSlot As<T>(T value) => Unsafe.As<T, TSlot>(ref value);
    }
}
