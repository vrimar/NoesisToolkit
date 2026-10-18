using System;
using NoesisToolkit.CodeGen;

namespace NoesisToolkit.Mvvm.CodeGen;

static class NativeSlots
{
    internal static bool Typed(Type type) => NoesisSlots.IsTyped(Underlying(type).FullName!);

    internal static bool Misrouted(Type type) =>
        NoesisSlots.IsMisrouted(Underlying(type).FullName!);

    static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}
