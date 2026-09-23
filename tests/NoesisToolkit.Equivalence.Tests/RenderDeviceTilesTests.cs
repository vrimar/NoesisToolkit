using Noesis;
using NoesisToolkit.Mvvm;
using Marshal = System.Runtime.InteropServices.Marshal;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class RenderDeviceTilesTests
{
    const int Warmup = 4;
    const int Frames = 16;

    sealed class StubTexture(uint width, uint height) : Texture
    {
        public override uint Width => width;
        public override uint Height => height;
        public override bool HasMipMaps => false;
        public override bool IsInverted => false;
        public override bool HasAlpha => true;
    }

    sealed class StubTarget(uint width, uint height) : RenderTarget
    {
        readonly StubTexture _texture = new(width, height);

        public override Texture Texture => _texture;
    }

    sealed class StubDevice : RenderDevice
    {
        const int Scratch = 1 << 20;
        readonly nint _vertices = Marshal.AllocHGlobal(Scratch);
        readonly nint _indices = Marshal.AllocHGlobal(Scratch);

        public int Resolves { get; private set; }
        public Tile Last { get; private set; }
        public int MostTiles { get; private set; }

        public override DeviceCaps Caps => new() { CenterPixelOffset = 0 };

        public override RenderTarget CreateRenderTarget(
            string label,
            uint width,
            uint height,
            uint sampleCount,
            bool needsStencil
        ) => new StubTarget(width, height);

        public override RenderTarget CloneRenderTarget(string label, RenderTarget surface) =>
            new StubTarget(surface.Texture.Width, surface.Texture.Height);

        public override Texture CreateTexture(
            string label,
            uint width,
            uint height,
            uint numLevels,
            TextureFormat format,
            nint data
        ) => new StubTexture(width, height);

        public override void ResolveRenderTarget(RenderTarget surface, Tile[] tiles)
        {
            Resolves++;
            MostTiles = Math.Max(MostTiles, tiles.Length);
            if (tiles.Length > 0)
                Last = tiles[^1];
        }

        public override void SetRenderTarget(RenderTarget surface) { }

        public override void BeginTile(RenderTarget surface, Tile tile) { }

        public override void EndTile(RenderTarget surface) { }

        public override void UpdateTexture(
            Texture texture,
            uint level,
            uint x,
            uint y,
            uint width,
            uint height,
            nint data
        ) { }

        public override void BeginOffscreenRender() { }

        public override void EndOffscreenRender() { }

        public override void BeginOnscreenRender() { }

        public override void EndOnscreenRender() { }

        public override nint MapVertices(uint bytes) => _vertices;

        public override void UnmapVertices() { }

        public override nint MapIndices(uint bytes) => _indices;

        public override void UnmapIndices() { }

        public override void DrawBatch(ref Batch batch) { }

        public void Free()
        {
            Marshal.FreeHGlobal(_vertices);
            Marshal.FreeHGlobal(_indices);
        }
    }

    // A group opacity over two children is drawn to an offscreen target and resolved once a frame.
    static (View View, StubDevice Device, Panel Root) Offscreen()
    {
        NoesisRuntime.Start();
        RenderDeviceTiles.Reuse();

        var root = new StackPanel { Width = 200, Height = 100 };
        root.Children.Add(Group());

        var view = GUI.CreateView(root);
        view.SetSize(200, 100);
        var device = new StubDevice();
        view.Renderer.Init(device);
        return (view, device, root);
    }

    static StackPanel Group()
    {
        var group = new StackPanel { Opacity = 0.5f };
        group.Children.Add(
            new Border
            {
                Width = 40,
                Height = 10,
                Background = Brushes.Red,
            }
        );
        group.Children.Add(
            new Border
            {
                Width = 40,
                Height = 10,
                Background = Brushes.Blue,
            }
        );
        return group;
    }

    static void Frame(View view, int i)
    {
        view.Update(i * 0.016);
        view.Renderer.UpdateRenderTree();
        view.Renderer.RenderOffscreen();
        view.Renderer.Render();
    }

    [Test]
    public async Task AnOffscreenPass_ResolvesWithoutAllocating()
    {
        var (view, device, _) = Offscreen();
        try
        {
            for (var i = 0; i < Warmup; i++)
                Frame(view, i);

            var resolvesBefore = device.Resolves;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = Warmup; i < Warmup + Frames; i++)
                Frame(view, i);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            await Assert
                .That(device.Resolves - resolvesBefore)
                .IsGreaterThanOrEqualTo(Frames)
                .Because("the group opacity takes an offscreen pass every frame");
            await Assert.That(device.Last.Width).IsGreaterThan(0u).And.IsLessThanOrEqualTo(200u);
            await Assert.That(device.Last.Height).IsGreaterThan(0u).And.IsLessThanOrEqualTo(100u);
            await Assert.That(allocated).IsEqualTo(0L);
        }
        finally
        {
            view.Renderer.Shutdown();
            device.Free();
        }
    }

    [Test]
    public async Task A_tile_count_never_resolved_before_resolves_without_allocating()
    {
        var (view, device, root) = Offscreen();
        try
        {
            for (var i = 0; i < Warmup; i++)
                Frame(view, i);

            var warmed = device.MostTiles;
            for (var g = 0; g < 4; g++)
                root.Children.Add(Group());

            view.Update(Warmup * 0.016);
            view.Renderer.UpdateRenderTree();

            var before = GC.GetAllocatedBytesForCurrentThread();
            view.Renderer.RenderOffscreen();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            await Assert.That(device.MostTiles).IsGreaterThan(warmed);
            await Assert.That(allocated).IsEqualTo(0L);
        }
        finally
        {
            view.Renderer.Shutdown();
            device.Free();
        }
    }
}
