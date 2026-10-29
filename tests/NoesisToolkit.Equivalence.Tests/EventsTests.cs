using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;
using NoesisToolkit.Testing;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class EventsTests
{
    const int Warmup = 8;
    const int Iterations = 64;

    static long Allocated(Action body) => AllocationCost.Of(body, Warmup, Iterations);

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
    public void A_key_handler_refuses_an_event_whose_args_carry_no_key()
    {
        NoesisRuntime.Start();

        var host = new SpikeControl();

        Assert.Throws<ArgumentException>(() =>
            Events.OnKey(host, UIElement.MouseDownEvent, static (_, _) => { })
        );
        Assert.Throws<ArgumentException>(() =>
            Events.OnKeyClaim(host, FrameworkElement.SizeChangedEvent, static (_, _) => true)
        );
    }

    [Test]
    public async Task A_key_left_unclaimed_lets_the_view_move_focus()
    {
        var (view, first, second) = ShowFocusPair(claim: false);

        view.KeyDown(Key.Down);

        await Assert.That(second.IsKeyboardFocused).IsTrue();
        await Assert.That(first.IsKeyboardFocused).IsFalse();
    }

    [Test]
    public async Task A_claimed_key_is_handled_so_the_view_keeps_focus()
    {
        var (view, first, _) = ShowFocusPair(claim: true);

        view.KeyDown(Key.Down);

        await Assert.That(first.IsKeyboardFocused).IsTrue();
        await Assert.That(Allocated(() => view.KeyDown(Key.Down))).IsEqualTo(0);
    }

    static (View View, SpikeControl First, SpikeControl Second) ShowFocusPair(bool claim)
    {
        NoesisRuntime.Start();

        var root = new StackPanel { Width = 400, Height = 300 };
        var first = new SpikeControl
        {
            Width = 100,
            Height = 100,
            Focusable = true,
        };
        var second = new SpikeControl
        {
            Width = 100,
            Height = 100,
            Focusable = true,
        };
        Events.OnKeyClaim(root, UIElement.KeyDownEvent, (_, key) => claim && key == Key.Down);

        var view = NoesisRuntime.Show(root, first, second);
        first.Focus();
        return (view, first, second);
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
    public async Task A_handler_taken_again_after_its_removal_runs_once()
    {
        NoesisRuntime.Start();

        var sized = 0;
        var host = new Border { Width = 100, Height = 100 };
        void OnSized(FrameworkElement _) => sized++;
        var view = NoesisRuntime.Show(host);

        Events.On(host, FrameworkElement.SizeChangedEvent, OnSized);
        Events.Off(host, FrameworkElement.SizeChangedEvent, OnSized);
        Events.On(host, FrameworkElement.SizeChangedEvent, OnSized);
        var before = sized;
        host.Width = 120;
        NoesisRuntime.Pump(view, host);

        await Assert.That(sized).IsEqualTo(before + 1);
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

    [Test]
    public void A_routed_event_subscribed_by_name_is_refused()
    {
        NoesisRuntime.Start();

        var host = new Border();

        Assert.Throws<ArgumentException>(() => Events.On(host, "SizeChanged", static _ => { }));
    }

    [Test]
    public async Task A_handler_on_an_element_a_compiled_binding_also_watches_runs_once()
    {
        NoesisRuntime.Start();

        var runs = 0;
        var target = new TextBlock { DataContext = new SpikeItem { Label = "x" } };
        CompiledBinding.Bind(
            target,
            TextBlock.TextProperty,
            new CompiledBindingSpec { Hops = [Hop.Label] }
        );
        Events.On(target, FrameworkElement.LoadedEvent, _ => runs++);

        NoesisRuntime.Show(target);

        await Assert.That(runs).IsEqualTo(1);
    }
}
