using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Mvvm;

/// <summary>Hands <see cref="RenderDevice.ResolveRenderTarget"/> a reused tile array. Noesis' own callback
/// declares its tiles as a marshalled array, so every resolve — one per offscreen pass, which every opacity
/// mask, effect and group opacity takes — mints a fresh one.</summary>
public static unsafe class RenderDeviceTiles
{
    [ThreadStatic]
    static Tile[]?[]? _bySize;

    static bool _reused;

    /// <summary>Re-registers Noesis' device callbacks with the resolve replaced and the rest its own. Call it
    /// once Noesis is initialized; calling it again does nothing.</summary>
    public static void Reuse()
    {
        if (_reused)
            return;

        RuntimeHelpers.RunClassConstructor(typeof(RenderDevice).TypeHandle);
        Noesis_RenderDevice_SetCallbacks(
            Pointer("_getCaps"),
            Pointer("_createRenderTarget"),
            Pointer("_cloneRenderTarget"),
            Pointer("_setRenderTarget"),
            Pointer("_beginTile"),
            Pointer("_endTile"),
            &Resolve,
            Pointer("_createTexture"),
            Pointer("_updateTexture"),
            Pointer("_beginOffscreenRender"),
            Pointer("_endOffscreenRender"),
            Pointer("_beginOnscreenRender"),
            Pointer("_endOnscreenRender"),
            Pointer("_mapVertices"),
            Pointer("_unmapVertices"),
            Pointer("_mapIndices"),
            Pointer("_unmapIndices"),
            Pointer("_drawBatch")
        );
        _reused = true;
    }

    internal static Tile[] Tiles(Tile* tiles, int count)
    {
        var bySize = _bySize;
        if (bySize is null || bySize.Length <= count)
        {
            Array.Resize(ref bySize, Math.Max(count + 1, 8));
            _bySize = bySize;
        }

        var array = bySize[count] ??= new Tile[count];
        new ReadOnlySpan<Tile>(tiles, count).CopyTo(array);
        return array;
    }

    // Noesis keeps these delegates in statics for the process, which is what keeps each pointer valid.
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Each delegate type is a parameter of Noesis' own SetCallbacks import, so its marshalling is generated ahead of time."
    )]
    static nint Pointer(string field) =>
        System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(
            (Delegate)
                typeof(RenderDevice)
                    .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!
                    .GetValue(null)!
        );

    [UnmanagedCallersOnly]
    static void Resolve(nint device, nint surface, Tile* tiles, int count)
    {
        try
        {
            if (NoesisInternals.ExtendInstance(null, device) is RenderDevice renderDevice)
                renderDevice.ResolveRenderTarget(
                    (RenderTarget)NoesisInternals.ExtendInstance(null, surface)!,
                    Tiles(tiles, count)
                );
        }
        catch (Exception exception)
        {
            Error.UnhandledException(exception);
        }
    }

    [DllImport("Noesis")]
    static extern void Noesis_RenderDevice_SetCallbacks(
        nint getCaps,
        nint createRenderTarget,
        nint cloneRenderTarget,
        nint setRenderTarget,
        nint beginTile,
        nint endTile,
        delegate* unmanaged<nint, nint, Tile*, int, void> resolveRenderTarget,
        nint createTexture,
        nint updateTexture,
        nint beginOffscreenRender,
        nint endOffscreenRender,
        nint beginOnscreenRender,
        nint endOnscreenRender,
        nint mapVertices,
        nint unmapVertices,
        nint mapIndices,
        nint unmapIndices,
        nint drawBatch
    );
}
