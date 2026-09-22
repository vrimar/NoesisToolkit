using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class TranslatePointTests
{
    const int Warmup = 8;
    const int Iterations = 64;

    static long Allocated(Action body)
    {
        for (var i = 0; i < Warmup; i++)
            body();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
            body();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    static (Border Child, Grid Root) Laid()
    {
        NoesisRuntime.Start();

        var child = new Border
        {
            Width = 40,
            Height = 20,
            Margin = new Thickness(11, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var root = new Grid { Width = 400, Height = 300 };
        NoesisRuntime.Show(root, child);
        root.UpdateLayout();
        return (child, root);
    }

    [Test]
    public async Task ATranslatedPoint_IsWhatNoesisReturns()
    {
        var (child, root) = Laid();

        var native = child.TranslatePoint(new Point(3, 5), root);
        var typed = ElementGeometry.TranslatePoint(child, new Point(3, 5), root);

        await Assert.That(typed.X).IsEqualTo(native.X);
        await Assert.That(typed.Y).IsEqualTo(native.Y);
        await Assert
            .That(typed.X)
            .IsEqualTo(14f)
            .Because("the margin puts the child at 11, and the point is 3 into it");
    }

    [Test]
    public async Task ASizeRead_IsWhatNoesisReturns()
    {
        var (child, _) = Laid();

        await Assert
            .That(ElementGeometry.DesiredSize(child).Width)
            .IsEqualTo(child.DesiredSize.Width);
        await Assert
            .That(ElementGeometry.DesiredSize(child).Height)
            .IsEqualTo(child.DesiredSize.Height);
        await Assert
            .That(ElementGeometry.RenderSize(child).Width)
            .IsEqualTo(child.RenderSize.Width);
        await Assert
            .That(ElementGeometry.RenderSize(child).Height)
            .IsEqualTo(child.RenderSize.Height);
        // The 40-wide child plus its 11 of left margin: a desired size carries the margin.
        await Assert.That(ElementGeometry.DesiredSize(child).Width).IsEqualTo(51f);
    }

    [Test]
    public async Task ASizeRead_CostsNothing()
    {
        var (child, _) = Laid();

        var native = Allocated(() => _ = child.DesiredSize);
        var typed = Allocated(() => ElementGeometry.DesiredSize(child));

        await Assert
            .That(native)
            .IsGreaterThan(0)
            .Because("Noesis' own read is what this exists to replace");
        await Assert.That(typed).IsEqualTo(0);
    }

    [Test]
    public async Task ATranslatedPoint_CostsNothing()
    {
        var (child, root) = Laid();

        var native = Allocated(() => child.TranslatePoint(new Point(), root));
        var typed = Allocated(() => ElementGeometry.TranslatePoint(child, new Point(), root));

        await Assert
            .That(native)
            .IsGreaterThan(0)
            .Because("Noesis' own read is what this exists to replace");
        await Assert.That(typed).IsEqualTo(0);
    }
}
