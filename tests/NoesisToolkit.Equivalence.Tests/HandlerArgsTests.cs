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
