using System.Runtime.CompilerServices;

namespace NoesisToolkit.Mvvm.CodeGen;

/// <summary>Noesis internals, reached through .NET 10 accessors.</summary>
static class NoesisInternals
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetProxy")]
    internal static extern object? Proxy(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? owner,
        nint cPtr,
        bool ownMemory
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetExtendInstance")]
    internal static extern object? ExtendInstance(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? owner,
        nint cPtr
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "get_Initialized")]
    internal static extern bool Initialized(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? owner
    );

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetEventId")]
    internal static extern uint EventId(
        [UnsafeAccessorType("Noesis.EventManager, Noesis.GUI")] object? owner,
        string name
    );
}
