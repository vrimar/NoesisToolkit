using System;
using System.Collections.Generic;
using Noesis;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>The property types Noesis' <c>GetValue</c> and <c>SetValue</c> marshal through a typed
/// native of their own; every other type goes through the object slot as a component.</summary>
static class NativeSlots
{
    static readonly HashSet<Type> Types = new HashSet<Type>
    {
        typeof(bool),
        typeof(bool?),
        typeof(byte),
        typeof(byte?),
        typeof(char),
        typeof(char?),
        typeof(sbyte),
        typeof(sbyte?),
        typeof(short),
        typeof(short?),
        typeof(ushort),
        typeof(ushort?),
        typeof(int),
        typeof(int?),
        typeof(uint),
        typeof(uint?),
        typeof(long),
        typeof(long?),
        typeof(ulong),
        typeof(ulong?),
        typeof(float),
        typeof(float?),
        typeof(double),
        typeof(double?),
        typeof(decimal),
        typeof(decimal?),
        typeof(string),
        typeof(Uri),
        typeof(Type),
        typeof(TimeSpan),
        typeof(TimeSpan?),
        typeof(Color),
        typeof(Color?),
        typeof(Point),
        typeof(Point?),
        typeof(Rect),
        typeof(Rect?),
        typeof(Int32Rect),
        typeof(Int32Rect?),
        typeof(Size),
        typeof(Size?),
        typeof(Thickness),
        typeof(Thickness?),
        typeof(CornerRadius),
        typeof(CornerRadius?),
        typeof(Duration),
        typeof(Duration?),
        typeof(KeyTime),
        typeof(KeyTime?),
    };

    /// <summary>True where Noesis marshals <paramref name="type"/> itself rather than as a
    /// component; an enum is marshalled as its bits, which the caller handles first.</summary>
    internal static bool Typed(Type type) => Types.Contains(type);
}
