using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Testing;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class TranslatePointTests
{
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

        var native = AllocationCost.Of(() => _ = child.DesiredSize);
        var typed = AllocationCost.Of(() => ElementGeometry.DesiredSize(child));

        await Assert
            .That(native)
            .IsGreaterThan(0)
            .Because("Noesis' own read is what this exists to replace");
        await Assert.That(typed).IsEqualTo(0);
    }

    [Test]
    public async Task AScreenPoint_IsWhatNoesisReturns()
    {
        var (child, _) = Laid();

        var toScreen = ElementGeometry.PointToScreen(child, new Point(3, 5));
        var fromScreen = ElementGeometry.PointFromScreen(child, new Point(20, 30));

        await Assert.That(toScreen.X).IsEqualTo(child.PointToScreen(new Point(3, 5)).X);
        await Assert.That(toScreen.Y).IsEqualTo(child.PointToScreen(new Point(3, 5)).Y);
        await Assert.That(fromScreen.X).IsEqualTo(child.PointFromScreen(new Point(20, 30)).X);
        await Assert.That(fromScreen.Y).IsEqualTo(child.PointFromScreen(new Point(20, 30)).Y);
        await Assert
            .That(toScreen.X)
            .IsEqualTo(14f)
            .Because("the margin puts the child at 11, and the point is 3 into it");
        await Assert.That(fromScreen.X).IsEqualTo(9f);
    }

    [Test]
    public async Task AScreenPoint_CostsNothing()
    {
        var (child, _) = Laid();

        var native = AllocationCost.Of(() => child.PointToScreen(new Point()));
        var toScreen = AllocationCost.Of(() => ElementGeometry.PointToScreen(child, new Point()));
        var fromScreen = AllocationCost.Of(() =>
            ElementGeometry.PointFromScreen(child, new Point())
        );

        await Assert
            .That(native)
            .IsGreaterThan(0)
            .Because("Noesis' own read is what this exists to replace");
        await Assert.That(toScreen).IsEqualTo(0);
        await Assert.That(fromScreen).IsEqualTo(0);
    }

    static (
        Point Native,
        Point Typed,
        long NativeCost,
        long TypedCost,
        Point RootNative,
        Point RootTyped
    ) MovedOver()
    {
        var child = new Border
        {
            Width = 40,
            Height = 20,
            Margin = new Thickness(11, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var root = new Grid
        {
            Width = 400,
            Height = 300,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        var view = NoesisRuntime.Show(root, child);

        var read = (default(Point), default(Point), -1L, -1L, default(Point), default(Point));
        root.PreviewMouseMove += (_, e) =>
            read = (
                e.GetPosition(child),
                ElementGeometry.GetPosition(e, child),
                AllocationCost.Of(() => e.GetPosition(child)),
                AllocationCost.Of(() => ElementGeometry.GetPosition(e, child)),
                e.GetPosition(null),
                ElementGeometry.GetPosition(e, null)
            );
        view.MouseMove(20, 30);
        return read;
    }

    [Test]
    public async Task APointerPosition_IsWhatNoesisReturns()
    {
        var (native, typed, _, _, rootNative, rootTyped) = MovedOver();

        await Assert.That(typed.X).IsEqualTo(native.X);
        await Assert.That(typed.Y).IsEqualTo(native.Y);
        await Assert.That(rootTyped.X).IsEqualTo(rootNative.X);
        await Assert.That(rootTyped.Y).IsEqualTo(rootNative.Y);
        await Assert
            .That(typed.X)
            .IsEqualTo(9f)
            .Because("the margin puts the child at 11, and the pointer is at 20");
    }

    [Test]
    public async Task APointerPosition_CostsNothing()
    {
        var (_, _, native, typed, _, _) = MovedOver();

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

        var native = AllocationCost.Of(() => child.TranslatePoint(new Point(), root));
        var typed = AllocationCost.Of(() =>
            ElementGeometry.TranslatePoint(child, new Point(), root)
        );

        await Assert
            .That(native)
            .IsGreaterThan(0)
            .Because("Noesis' own read is what this exists to replace");
        await Assert.That(typed).IsEqualTo(0);
    }
}
