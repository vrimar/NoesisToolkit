using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Testing;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class HandlerArgsTests
{
    const int Warmup = 8;
    const int Iterations = 64;

    static long Allocated(Action body) => AllocationCost.Of(body, Warmup, Iterations);

    static void Start()
    {
        NoesisRuntime.Start();
        HandlerArgs.Reuse();
    }

    // The renderless harness hit-tests nothing, so the routed event is raised on the element itself.
    [Test]
    public async Task A_managed_mouse_handler_is_delivered_without_allocating()
    {
        Start();

        var entered = 0;
        var host = new Border { Width = 100, Height = 100 };
        host.MouseEnter += (_, _) => entered++;
        host.MouseLeave += (_, _) => { };
        NoesisRuntime.Show(host);

        var enter = new MouseEventArgs(host, UIElement.MouseEnterEvent);
        var leave = new MouseEventArgs(host, UIElement.MouseLeaveEvent);
        var crossing = Allocated(() =>
        {
            host.RaiseEvent(enter);
            host.RaiseEvent(leave);
        });

        await Assert.That(crossing).IsEqualTo(0);
        await Assert.That(entered).IsEqualTo(Warmup + Iterations);
    }

    [Test]
    public async Task Managed_drag_handlers_are_delivered_without_allocating()
    {
        Start();

        var target = new Border
        {
            Width = 200,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Transparent),
            AllowDrop = true,
        };
        var root = new Grid
        {
            Width = 400,
            Height = 300,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        var view = NoesisRuntime.Show(root, target);

        var dragging = false;
        root.PreviewMouseMove += (_, e) =>
        {
            if (dragging || e.LeftButton != MouseButtonState.Pressed)
                return;

            dragging = true;
            DragDrop.DoDragDrop(root, "payload", DragDropEffects.Move);
        };

        var over = 0;
        var feedback = 0;
        target.DragOver += (_, e) =>
        {
            over++;
            e.Effects = DragDropEffects.Move;
        };
        root.GiveFeedback += (_, e) =>
        {
            feedback++;
            e.UseDefaultCursors = false;
        };

        view.MouseMove(20, 30);
        view.MouseButtonDown(20, 30, MouseButton.Left);
        view.MouseMove(24, 30);

        var x = 24;
        var moving = Allocated(() => view.MouseMove(x = x == 24 ? 28 : 24, 30));
        view.MouseButtonUp(x, 30, MouseButton.Left);

        await Assert.That(over).IsGreaterThanOrEqualTo(Warmup + Iterations);
        await Assert.That(feedback).IsGreaterThanOrEqualTo(Warmup + Iterations);
        await Assert.That(moving).IsEqualTo(0);
    }

    static DragDropEffects _ended;

    static readonly NativeDrag.Completed Ended = static (effects, _) => _ended = effects;

    [Test]
    public async Task A_native_drag_reports_its_drop_without_allocating()
    {
        Start();

        var target = new Border
        {
            Width = 200,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Transparent),
            AllowDrop = true,
        };
        var root = new Grid
        {
            Width = 400,
            Height = 300,
            Background = new SolidColorBrush(Colors.Transparent),
        };
        var view = NoesisRuntime.Show(root, target);

        var payload = new Border();
        var dragging = false;
        root.PreviewMouseMove += (_, e) =>
        {
            if (dragging || e.LeftButton != MouseButtonState.Pressed)
                return;

            dragging = true;
            NativeDrag.DoDragDrop(root, payload, DragDropEffects.Move, Ended);
        };
        root.PreviewMouseLeftButtonUp += (_, _) => dragging = false;
        target.Drop += (_, e) => e.Effects = DragDropEffects.Move;

        var gesture = Allocated(() =>
        {
            _ended = DragDropEffects.None;
            view.MouseMove(20, 30);
            view.MouseButtonDown(20, 30, MouseButton.Left);
            view.MouseMove(24, 30);
            view.MouseMove(28, 30);
            view.MouseButtonUp(28, 30, MouseButton.Left);
        });

        await Assert.That(_ended).IsEqualTo(DragDropEffects.Move);
        await Assert.That(gesture).IsEqualTo(0);
    }

    [Test]
    public async Task A_managed_key_handler_reads_the_native_args()
    {
        Start();

        var seen = Key.None;
        var bubbled = 0;
        var host = new SpikeControl
        {
            Width = 100,
            Height = 100,
            Focusable = true,
        };
        host.KeyDown += (_, e) =>
        {
            seen = e.Key;
            e.Handled = true;
        };
        var root = new StackPanel { Width = 400, Height = 300 };
        root.KeyDown += (_, _) => bubbled++;

        var view = NoesisRuntime.Show(root, host);
        host.Focus();
        view.KeyDown(Key.Enter);

        await Assert.That(seen).IsEqualTo(Key.Enter);
        await Assert
            .That(bubbled)
            .IsEqualTo(0)
            .Because("the handler marked the native args handled");
        await Assert.That(Allocated(() => view.KeyDown(Key.Enter))).IsEqualTo(0);
    }

    [Test]
    public async Task A_handler_that_raises_its_own_event_type_keeps_its_args()
    {
        Start();

        var outer = new Button();
        var inner = new Button();
        object? innerSource = null;
        object? outerSourceAfter = null;
        var nested = new RoutedEventArgs(ButtonBase.ClickEvent, inner);

        inner.Click += (_, e) => innerSource = e.Source;
        outer.Click += (_, e) =>
        {
            inner.RaiseEvent(nested);
            outerSourceAfter = e.Source;
        };
        NoesisRuntime.Show(outer, inner);

        outer.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, outer));

        await Assert.That(innerSource).IsSameReferenceAs(inner);
        await Assert.That(outerSourceAfter).IsSameReferenceAs(outer);
    }
}
