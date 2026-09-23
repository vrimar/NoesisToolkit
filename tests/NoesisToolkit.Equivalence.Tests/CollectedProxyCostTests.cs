using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CollectedProxyCostTests
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

    [Test]
    public async Task A_template_trigger_on_native_elements_mints_no_proxy_after_a_collection()
    {
        NoesisRuntime.Start();

        var driver = new Border { HorizontalAlignment = HorizontalAlignment.Left };
        var (view, root) = ShowAligned(driver);
        void Flip() =>
            driver.HorizontalAlignment =
                driver.HorizontalAlignment == HorizontalAlignment.Right
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Right;

        Cost(Flip, collect: true);
        var warm = Cost(Flip, collect: false);
        var cold = Cost(Flip, collect: true);
        NoesisRuntime.Pump(view, root);

        await Assert.That(cold).IsEqualTo(warm);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, Grid Root) ShowTitle(LifetimeOwner owner)
    {
        var fixture = new LifetimeFixture { DataContext = owner };
        fixture.InitializeComponent();
        var root = new Grid { Width = 400, Height = 300 };
        return (NoesisRuntime.Show(root, fixture), root);
    }

    [Test]
    public async Task A_data_context_binding_on_a_native_element_mints_no_proxy_after_a_collection()
    {
        NoesisRuntime.Start();

        var owner = new LifetimeOwner { Title = "one" };
        var (view, root) = ShowTitle(owner);
        void Flip() => owner.Title = owner.Title == "one" ? "two" : "one";

        Cost(Flip, collect: true);
        var warm = Cost(Flip, collect: false);
        var cold = Cost(Flip, collect: true);
        NoesisRuntime.Pump(view, root);

        await Assert.That(cold).IsEqualTo(warm);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (View View, UIElementCollection Children, ListBox List) ShowBoundRows()
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
        return (view, root.Children, list);
    }

    static void Reload(View view, UIElementCollection children, ListBox list)
    {
        children.Remove(list);
        view.Update(0);
        children.Add(list);
        view.Update(0);
    }

    [Test]
    public async Task A_row_template_trigger_mints_no_proxy_when_its_row_reloads_after_a_collection()
    {
        NoesisRuntime.Start();

        var (view, children, list) = ShowBoundRows();
        void Cycle() => Reload(view, children, list);

        Cost(Cycle, collect: true);
        var warm = Cost(Cycle, collect: false);
        var cold = Cost(Cycle, collect: true);

        await Assert.That(cold).IsEqualTo(warm);
    }
}
