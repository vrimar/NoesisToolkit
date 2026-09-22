using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class EventsTests
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

    // The renderless harness hit-tests nothing, so the routed event is raised on the element itself.
    [Test]
    public async Task A_routed_event_reaches_its_handlers_without_allocating()
    {
        NoesisRuntime.Start();

        var entered = 0;
        var left = 0;
        var host = new Border { Width = 100, Height = 100 };
        Events.On(host, UIElement.MouseEnterEvent, _ => entered++);
        Events.On(host, UIElement.MouseLeaveEvent, _ => left++);
        NoesisRuntime.Show(host);

        var enter = new MouseEventArgs(host, UIElement.MouseEnterEvent);
        var leave = new MouseEventArgs(host, UIElement.MouseLeaveEvent);
        host.RaiseEvent(enter);
        host.RaiseEvent(leave);
        await Assert.That(entered).IsEqualTo(1);
        await Assert.That(left).IsEqualTo(1);

        var crossing = Allocated(() =>
        {
            host.RaiseEvent(enter);
            host.RaiseEvent(leave);
        });

        await Assert.That(crossing).IsEqualTo(0);
        await Assert.That(entered).IsEqualTo(1 + Warmup + Iterations);
    }

    [Test]
    public async Task A_key_handler_reads_the_key_off_the_native_args()
    {
        NoesisRuntime.Start();

        var seen = Key.None;
        var host = new SpikeControl
        {
            Width = 100,
            Height = 100,
            Focusable = true,
        };
        Events.OnKey(host, UIElement.KeyDownEvent, (_, key) => seen = key);

        var view = NoesisRuntime.Show(host);
        host.Focus();
        view.KeyDown(Key.Enter);

        await Assert.That(seen).IsEqualTo(Key.Enter);
        await Assert.That(Allocated(() => view.KeyDown(Key.Enter))).IsEqualTo(0);
    }

    [Test]
    public async Task A_routed_size_change_reaches_its_handler_and_stops_when_removed()
    {
        NoesisRuntime.Start();

        var sized = 0;
        var host = new Border { Width = 100, Height = 100 };
        void OnSized(FrameworkElement _) => sized++;
        Events.On(host, FrameworkElement.SizeChangedEvent, OnSized);

        var view = NoesisRuntime.Show(host);
        var first = sized;
        host.Width = 120;
        NoesisRuntime.Pump(view, host);
        await Assert.That(sized).IsEqualTo(first + 1);

        Events.Off(host, FrameworkElement.SizeChangedEvent, OnSized);
        host.Width = 140;
        NoesisRuntime.Pump(view, host);
        await Assert.That(sized).IsEqualTo(first + 1);
    }

    [Test]
    public async Task A_named_visibility_change_reaches_its_handler()
    {
        NoesisRuntime.Start();

        var changes = 0;
        var host = new Border { Width = 100, Height = 100 };
        Events.On(host, "IsVisibleChanged", _ => changes++);

        var view = NoesisRuntime.Show(host);
        var shown = changes;
        host.Visibility = Visibility.Collapsed;
        NoesisRuntime.Pump(view, host);
        host.Visibility = Visibility.Visible;
        NoesisRuntime.Pump(view, host);

        await Assert.That(changes).IsEqualTo(shown + 2);
    }
}
