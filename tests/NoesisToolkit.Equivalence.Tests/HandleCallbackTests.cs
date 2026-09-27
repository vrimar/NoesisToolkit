using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public static partial class HandleMarks
{
    public static ElementHandle Last;
    public static int Changes;

    [DependencyProperty(0, nameof(OnByHandleChanged))]
    public static partial int ByHandle { get; set; }

    [DependencyProperty(0, nameof(OnByObjectChanged))]
    public static partial int ByObject { get; set; }

    [DependencyProperty(0L)]
    public static partial long Wide { get; set; }

    [DependencyProperty(0f)]
    public static partial float Ratio { get; set; }

    [DependencyProperty(0d)]
    public static partial double Scale { get; set; }

    static void OnByHandleChanged(ElementHandle element, DependencyPropertyChangedEventArgs e)
    {
        Last = element;
        Changes++;
    }

    static void OnByObjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        Changes++;
}

[NotInParallel("Noesis")]
public sealed class HandleCallbackTests
{
    const int Times = 16;

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static long Cost(Action change, bool collect)
    {
        var total = 0L;
        for (var i = 0; i < Times; i++)
        {
            if (collect)
                Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            change();
            total += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return total;
    }

    // Returns the border by handle only, so nothing managed holds its proxy past the call.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, StackPanel Root, ElementHandle Border) ShowBorder()
    {
        var border = new Border { Width = 40, Height = 40 };
        var root = new StackPanel { Width = 400, Height = 300 };
        return (NoesisRuntime.Show(root, border), root, ElementHandle.Of(border));
    }

    [Test]
    public async Task A_handle_callback_runs_with_the_element_the_property_changed_on()
    {
        NoesisRuntime.Start();

        var border = new Border();
        NoesisRuntime.Show(border);
        HandleMarks.SetByHandle(border, 3);

        await Assert.That(HandleMarks.Last).IsEqualTo(ElementHandle.Of(border));
        await Assert.That(HandleMarks.Last.IsFrameworkElement).IsTrue();
        await Assert.That(HandleMarks.Last.Object).IsSameReferenceAs(border);
        await Assert.That(HandleMarks.GetByHandle(HandleMarks.Last)).IsEqualTo(3);
    }

    [Test]
    public async Task A_write_by_handle_lands_in_the_slot_of_its_own_type()
    {
        NoesisRuntime.Start();

        var border = new Border();
        NoesisRuntime.Show(border);
        var handle = ElementHandle.Of(border);
        DependencyWrite.Value(handle, HandleMarks.ByObjectProperty, 7);
        DependencyWrite.Value(handle, HandleMarks.WideProperty, 5_000_000_000L);
        DependencyWrite.Value(handle, HandleMarks.RatioProperty, 0.25f);
        DependencyWrite.Value(handle, HandleMarks.ScaleProperty, 1.5d);

        await Assert.That(HandleMarks.GetByObject(border)).IsEqualTo(7);
        await Assert.That(HandleMarks.GetWide(border)).IsEqualTo(5_000_000_000L);
        await Assert.That(HandleMarks.GetRatio(border)).IsEqualTo(0.25f);
        await Assert.That(HandleMarks.GetScale(border)).IsEqualTo(1.5d);
    }

    [Test]
    public async Task A_handle_callback_on_a_native_element_mints_no_proxy_after_a_collection()
    {
        NoesisRuntime.Start();

        var (view, root, border) = ShowBorder();
        var next = 0;
        HandleMarks.Changes = 0;
        void Change() => DependencyWrite.Value(border, HandleMarks.ByHandleProperty, ++next);

        Cost(Change, collect: true);
        var warm = Cost(Change, collect: false);
        var cold = Cost(Change, collect: true);
        NoesisRuntime.Pump(view, root);

        await Assert.That(HandleMarks.Changes).IsEqualTo(3 * Times);
        await Assert.That(cold).IsEqualTo(warm);
    }

    [Test]
    public async Task A_callback_that_takes_the_object_mints_its_proxy_after_a_collection()
    {
        NoesisRuntime.Start();

        var (view, root, border) = ShowBorder();
        var next = 0;
        void Change() => DependencyWrite.Value(border, HandleMarks.ByObjectProperty, ++next);

        Cost(Change, collect: true);
        var warm = Cost(Change, collect: false);
        var cold = Cost(Change, collect: true);
        NoesisRuntime.Pump(view, root);

        await Assert.That(cold).IsGreaterThan(warm);
    }

    [Test]
    public async Task An_event_subscribed_by_handle_reaches_the_element_and_stops_when_removed()
    {
        NoesisRuntime.Start();

        var host = new Border { Width = 100, Height = 100 };
        NoesisRuntime.Show(host);
        FrameworkElement? reached = null;
        var entered = 0;
        void Enter(FrameworkElement element)
        {
            reached = element;
            entered++;
        }

        Events.On(ElementHandle.Of(host), UIElement.MouseEnterEvent, Enter);
        Events.On(ElementHandle.Of(host), UIElement.MouseEnterEvent, Enter);
        var enter = new MouseEventArgs(host, UIElement.MouseEnterEvent);
        host.RaiseEvent(enter);
        Events.Off(ElementHandle.Of(host), UIElement.MouseEnterEvent, Enter);
        host.RaiseEvent(enter);

        await Assert.That(entered).IsEqualTo(1);
        await Assert.That(reached).IsSameReferenceAs(host);
    }

    [Test]
    public async Task Subscribing_by_handle_mints_no_proxy_after_a_collection()
    {
        NoesisRuntime.Start();

        var (view, root, border) = ShowBorder();
        var enter = UIElement.MouseEnterEvent;
        Action<FrameworkElement> handler = static _ => { };
        void Subscribe() => Events.On(border, enter, handler);

        Cost(Subscribe, collect: true);
        var warm = Cost(Subscribe, collect: false);
        var cold = Cost(Subscribe, collect: true);
        NoesisRuntime.Pump(view, root);

        await Assert.That(cold).IsEqualTo(warm);
    }

    [Test]
    public async Task Visibility_set_by_handle_takes_and_mints_no_proxy_after_a_collection()
    {
        NoesisRuntime.Start();

        var (view, root, border) = ShowBorder();
        var collapsed = false;
        void Flip() =>
            DependencyWrite.Visibility(
                border,
                (collapsed = !collapsed) ? Visibility.Collapsed : Visibility.Visible
            );

        Flip();
        var shown = ((UIElement)border.Object!).Visibility;
        Cost(Flip, collect: true);
        var warm = Cost(Flip, collect: false);
        var cold = Cost(Flip, collect: true);
        NoesisRuntime.Pump(view, root);

        await Assert.That(shown).IsEqualTo(Visibility.Collapsed);
        await Assert.That(cold).IsEqualTo(warm);
    }

    [Test]
    public void Subscribing_by_a_handle_that_names_no_element_is_refused()
    {
        NoesisRuntime.Start();

        var brush = new SolidColorBrush();

        Assert.Throws<ArgumentException>(() =>
            Events.On(ElementHandle.Of(brush), UIElement.MouseEnterEvent, static _ => { })
        );
    }
}
