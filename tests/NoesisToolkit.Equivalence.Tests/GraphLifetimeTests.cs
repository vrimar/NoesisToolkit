using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class LifetimeOwner : INotifyPropertyChanged
{
    string _title = "";
    PlainItem? _current;

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            Raise();
        }
    }

    public PlainItem? Current
    {
        get => _current;
        set
        {
            _current = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PlainItem
{
    public string Label { get; init; } = "";
}

public partial class LifetimeFixture : UserControl { }

public static partial class LifetimeMarks
{
    [DependencyProperty(HorizontalAlignment.Stretch)]
    public static partial HorizontalAlignment Side { get; set; }

    public static int Counted;

    [DependencyProperty(HorizontalAlignment.Stretch, nameof(OnCountedSideChanged))]
    public static partial HorizontalAlignment CountedSide { get; set; }

    static void OnCountedSideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        Counted++;
}

// Helpers stay out of line so no stack slot of a test holds a proxy across its collections.
[NotInParallel("Noesis")]
public sealed class GraphLifetimeTests
{
    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static void Settle(View view, FrameworkElement root, Func<bool> done)
    {
        for (var i = 0; i < 16 && !done(); i++)
        {
            Collect();
            NoesisRuntime.Pump(view, root);
        }
    }

    static long Toggle(Border driver, int times)
    {
        var total = 0L;
        for (var i = 0; i < times; i++)
        {
            Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            driver.HorizontalAlignment =
                driver.HorizontalAlignment == HorizontalAlignment.Right
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Right;
            total += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        return total;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, StackPanel Root) ShowAligned(Border driver)
    {
        var compiled =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesLifetimeChromexamlXaml.Build();

        var host = new ContentControl { Template = (ControlTemplate)compiled["Aligned"] };
        host.SetBinding(
            Control.HorizontalContentAlignmentProperty,
            new Binding(FrameworkElement.HorizontalAlignmentProperty) { Source = driver }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        return (NoesisRuntime.Show(root, driver, host), root);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Color ChromeColor(StackPanel root) =>
        ((SolidColorBrush)TreeSearch.Named<Border>(root, "Chrome")!.Background).Color;

    [Test]
    public async Task A_template_trigger_on_native_elements_keeps_working_across_collections()
    {
        NoesisRuntime.Start();

        var driver = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        var (view, root) = ShowAligned(driver);

        Toggle(driver, 7);
        NoesisRuntime.Pump(view, root);
        var afterOdd = ChromeColor(root);

        Toggle(driver, 1);
        NoesisRuntime.Pump(view, root);
        var afterEven = ChromeColor(root);

        await Assert.That(afterOdd).IsEqualTo(Colors.Green);
        await Assert.That(afterEven).IsEqualTo(Colors.Red);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference TrackedSet(StackPanel root) =>
        new WeakReference(CompiledTriggerSet.SetsOf(TreeSearch.Named<Border>(root, "Chrome")!)[0]);

    [Test]
    public async Task A_trigger_set_lives_as_long_as_its_element_not_its_proxy()
    {
        NoesisRuntime.Start();

        var driver = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        var (view, root) = ShowAligned(driver);
        var set = TrackedSet(root);

        Collect();
        NoesisRuntime.Pump(view, root);

        await Assert.That(set.IsAlive).IsTrue();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, Grid Root) ShowHoverList()
    {
        var compiled =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesLifetimeChromexamlXaml.Build();
        var list = new ListBox
        {
            ItemContainerStyle = (Style)compiled["HoverItem"],
            ItemsSource = new[] { "one", "two", "three" },
            VerticalAlignment = VerticalAlignment.Top,
        };
        var root = new Grid { Width = 400, Height = 300 };
        root.Children.Add(list);
        var view = GUI.CreateView(root);
        view.SetSize(400, 300);
        NoesisRuntime.Pump(view, root);
        return (view, root);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string RowColors(Grid root) =>
        string.Join(
            ",",
            TreeSearch
                .All<Border>(root)
                .Where(b => b.Name == "Row")
                .Select(b => ((SolidColorBrush)b.Background).Color.ToString())
        );

    [Test]
    public async Task A_list_row_hover_trigger_survives_a_collection()
    {
        NoesisRuntime.Start();
        var (view, root) = ShowHoverList();

        view.MouseMove(10, 10);
        NoesisRuntime.Pump(view, root);
        var hovered = RowColors(root);
        view.MouseMove(390, 290);
        NoesisRuntime.Pump(view, root);

        Collect();
        view.MouseMove(10, 10);
        NoesisRuntime.Pump(view, root);

        await Assert.That(hovered).IsEqualTo("#FF008000,#FFFF0000,#FFFF0000");
        await Assert.That(RowColors(root)).IsEqualTo(hovered);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, StackPanel Root) ShowMarked(Border driver, DependencyProperty mark)
    {
        var marked = new Border();
        marked.SetBinding(
            mark,
            new Binding(FrameworkElement.HorizontalAlignmentProperty) { Source = driver }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        return (NoesisRuntime.Show(root, driver, marked), root);
    }

    [Test]
    public async Task A_generated_attached_property_nobody_watches_allocates_nothing_after_a_collection()
    {
        NoesisRuntime.Start();

        var driver = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        var (view, root) = ShowMarked(driver, LifetimeMarks.SideProperty);
        Toggle(driver, 4);

        var bytes = Toggle(driver, 16);
        NoesisRuntime.Pump(view, root);

        await Assert.That(bytes).IsEqualTo(0);
    }

    [Test]
    public async Task A_generated_property_callback_runs_for_every_change_across_collections()
    {
        NoesisRuntime.Start();

        var driver = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        var (view, root) = ShowMarked(driver, LifetimeMarks.CountedSideProperty);
        Toggle(driver, 4);

        LifetimeMarks.Counted = 0;
        Toggle(driver, 16);
        NoesisRuntime.Pump(view, root);

        await Assert.That(LifetimeMarks.Counted).IsEqualTo(16);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, Grid Root, LifetimeFixture Fixture) ShowFixture(LifetimeOwner owner)
    {
        var fixture = new LifetimeFixture { DataContext = owner };
        fixture.InitializeComponent();
        var root = new Grid { Width = 400, Height = 300 };
        return (NoesisRuntime.Show(root, fixture), root, fixture);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string HeldText(LifetimeFixture fixture) => ((TextBlock)fixture.Holder.Content).Text;

    [Test]
    public async Task A_binding_on_a_reloaded_element_resumes_after_a_collection()
    {
        NoesisRuntime.Start();

        var owner = new LifetimeOwner { Title = "one" };
        var (view, root, fixture) = ShowFixture(owner);

        fixture.Panel.Children.Remove(fixture.Holder);
        NoesisRuntime.Pump(view, root);
        Collect();
        fixture.Panel.Children.Insert(0, fixture.Holder);
        NoesisRuntime.Pump(view, root);

        owner.Title = "two";

        await Assert.That(HeldText(fixture)).IsEqualTo("two");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string SlotText(LifetimeFixture fixture) =>
        TreeSearch.All<TextBlock>(fixture.Slot).Single().Text;

    [Test]
    public async Task A_template_binding_over_an_item_that_raises_nothing_follows_the_next_item_after_a_collection()
    {
        NoesisRuntime.Start();

        var owner = new LifetimeOwner { Current = new PlainItem { Label = "first" } };
        var (view, root, fixture) = ShowFixture(owner);
        var first = SlotText(fixture);

        Collect();
        owner.Current = new PlainItem { Label = "second" };
        NoesisRuntime.Pump(view, root);

        await Assert.That(first).IsEqualTo("first");
        await Assert.That(SlotText(fixture)).IsEqualTo("second");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static StrongBox<bool> WatchEnd(DependencyObject element)
    {
        var destroyed = new StrongBox<bool>();
        element.Destroyed += _ => destroyed.Value = true;
        return destroyed;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static StrongBox<bool> WatchEditor(LifetimeFixture fixture) =>
        WatchEnd(fixture.Editors.Children[0]);

    [Test]
    public async Task A_box_that_writes_back_on_blur_is_destroyed_once_dropped()
    {
        NoesisRuntime.Start();

        var owner = new LifetimeOwner { Title = "one" };
        var (view, root, fixture) = ShowFixture(owner);
        var destroyed = WatchEditor(fixture);

        fixture.Editors.Children.Clear();
        Settle(view, root, () => destroyed.Value);

        await Assert.That(destroyed.Value).IsTrue();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (WeakReference Binding, StrongBox<bool> Destroyed) BindLabel(StackPanel root)
    {
        var text = new TextBlock { DataContext = new PlainItem { Label = "plain" } };
        root.Children.Add(text);
        var binding = CompiledBinding.Bind(
            text,
            TextBlock.TextProperty,
            new CompiledBindingSpec
            {
                Hops = [new BindingHop(nameof(PlainItem.Label), static o => ((PlainItem)o).Label)],
            }
        );

        return (new WeakReference(binding), WatchEnd(text));
    }

    [Test]
    public async Task A_compiled_binding_lives_exactly_as_long_as_its_element()
    {
        NoesisRuntime.Start();

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root);
        var (binding, destroyed) = BindLabel(root);

        Collect();
        NoesisRuntime.Pump(view, root);
        var aliveWithElement = binding.IsAlive;

        root.Children.Clear();
        Settle(view, root, () => destroyed.Value && !binding.IsAlive);

        await Assert.That(aliveWithElement).IsTrue();
        await Assert.That(destroyed.Value).IsTrue();
        await Assert.That(binding.IsAlive).IsFalse();
    }

    static FrameworkElement? SiblingSource(FrameworkElement target)
    {
        if (target.Parent is not Panel panel)
            return null;

        foreach (var child in panel.Children)
        {
            if (child is Border { Name: "Source" } source)
                return source;
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static StrongBox<bool> BindThroughSibling(StackPanel root, LifetimeOwner owner)
    {
        var source = new Border { Name = "Source", Tag = owner };
        var text = new TextBlock();
        root.Children.Add(source);
        root.Children.Add(text);

        CompiledBinding.Bind(
            text,
            TextBlock.TextProperty,
            new CompiledBindingSpec
            {
                Source = SiblingSource,
                SourceProperty = FrameworkElement.TagProperty,
                Hops =
                [
                    new BindingHop(
                        nameof(LifetimeOwner.Title),
                        static o => ((LifetimeOwner)o).Title
                    ),
                ],
            }
        );

        return WatchEnd(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void DropSource(StackPanel root) => root.Children.RemoveAt(0);

    [Test]
    public async Task A_source_destroyed_before_its_target_is_never_read_again()
    {
        NoesisRuntime.Start();

        var owner = new LifetimeOwner { Title = "one" };
        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root);
        var sourceDestroyed = BindThroughSibling(root, owner);
        NoesisRuntime.Pump(view, root);
        var before = TextOf(root, 1);

        DropSource(root);
        Settle(view, root, () => sourceDestroyed.Value);

        owner.Title = "two";
        NoesisRuntime.Pump(view, root);

        await Assert.That(before).IsEqualTo("one");
        await Assert.That(sourceDestroyed.Value).IsTrue();
        await Assert.That(TextOf(root, 0)).IsEqualTo("");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static string TextOf(StackPanel root, int index) => ((TextBlock)root.Children[index]).Text;

    static int _entered;
    static int _moved;
    static readonly Action<FrameworkElement> Entered = static _ => _entered++;
    static readonly Action<FrameworkElement> Moved = static _ => _moved++;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, StackPanel Root, StrongBox<bool> Destroyed) ShowSubscribed()
    {
        var host = new Border { Width = 100, Height = 100 };
        Events.On(host, UIElement.MouseEnterEvent, Entered);
        DependencyWatcher.Watch(host, FrameworkElement.HorizontalAlignmentProperty, Moved);
        var root = new StackPanel { Width = 400, Height = 300 };
        return (NoesisRuntime.Show(root, host), root, WatchEnd(host));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void EnterAndMove(StackPanel root)
    {
        var host = (Border)root.Children[0];
        host.RaiseEvent(new MouseEventArgs(host, UIElement.MouseEnterEvent));
        host.HorizontalAlignment =
            host.HorizontalAlignment == HorizontalAlignment.Left
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;
    }

    [Test]
    public async Task A_subscription_on_a_native_element_survives_a_collection()
    {
        NoesisRuntime.Start();
        var (view, root, _) = ShowSubscribed();
        _entered = 0;
        _moved = 0;

        EnterAndMove(root);
        Collect();
        NoesisRuntime.Pump(view, root);
        EnterAndMove(root);

        await Assert.That(_entered).IsEqualTo(2);
        await Assert.That(_moved).IsEqualTo(2);
    }

    [Test]
    public async Task An_element_with_subscriptions_is_destroyed_once_dropped()
    {
        NoesisRuntime.Start();
        var (view, root, destroyed) = ShowSubscribed();

        root.Children.Clear();
        Settle(view, root, () => destroyed.Value);

        await Assert.That(destroyed.Value).IsTrue();
    }

    [Test]
    public async Task Subscribing_the_same_handler_again_delivers_it_once()
    {
        NoesisRuntime.Start();
        var (view, root, _) = ShowSubscribed();
        var host = (Border)root.Children[0];
        Events.On(host, UIElement.MouseEnterEvent, Entered);
        DependencyWatcher.Watch(host, FrameworkElement.HorizontalAlignmentProperty, Moved);
        _entered = 0;
        _moved = 0;

        EnterAndMove(root);
        NoesisRuntime.Pump(view, root);

        await Assert.That(_entered).IsEqualTo(1);
        await Assert.That(_moved).IsEqualTo(1);
    }
}
