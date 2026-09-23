using System.ComponentModel;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class SettledCostTests
{
    sealed class Toggle : INotifyPropertyChanged
    {
        static readonly PropertyChangedEventArgs OnChanged = new(nameof(On));
        public static readonly object True = true;
        public static readonly object False = false;

        bool _on;

        public bool On
        {
            get => _on;
            set
            {
                _on = value;
                PropertyChanged?.Invoke(this, OnChanged);
            }
        }

        public object Boxed => _on ? True : False;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

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

    static CompiledTriggerCondition When(object value) =>
        new()
        {
            Part = new CompiledBindingPart
            {
                Hops = [new BindingHop(nameof(Toggle.On), static o => ((Toggle)o).Boxed)],
            },
            Value = value,
        };

    [Test]
    public async Task Re_evaluating_a_settled_trigger_set_allocates_nothing()
    {
        NoesisRuntime.Start();

        var loose = new Toggle();
        var floor = Allocated(() => loose.On = !loose.On);

        var item = new Toggle();
        var host = new SpikeControl { DataContext = item };
        var setter = new CompiledSetter { Property = SpikeControl.LabelProperty, Value = "held" };
        CompiledTriggerSet.Bind(
            host,
            [
                new CompiledTriggerSpec { Conditions = [When(Toggle.True)], Setters = [setter] },
                new CompiledTriggerSpec { Conditions = [When(Toggle.False)], Setters = [setter] },
            ]
        );
        await Assert.That(host.Label).IsEqualTo("held");

        var bound = Allocated(() => item.On = !item.On);

        await Assert.That(bound).IsEqualTo(floor);
        await Assert.That(host.Label).IsEqualTo("held");
    }

    [Test]
    public async Task A_value_hop_boxes_again_only_when_the_value_moves()
    {
        var item = new SpikeItem { Index = 3 };
        var hop = BindingHop.Value<int>(nameof(SpikeItem.Index), static o => ((SpikeItem)o).Index);

        var first = hop.Read(item);
        await Assert.That(first).IsEqualTo(3);
        await Assert.That(Allocated(() => hop.Read(item))).IsEqualTo(0);
        await Assert.That(hop.Read(item)).IsSameReferenceAs(first);

        item.Index = 4;
        await Assert.That(hop.Read(item)).IsEqualTo(4);

        var flag = BindingHop.Bool(nameof(SpikeItem.Flag), static o => ((SpikeItem)o).Flag);
        var loose = new SpikeItem();
        await Assert
            .That(Allocated(() => flag.Read(loose = new SpikeItem { Flag = !loose.Flag })))
            .IsEqualTo(Allocated(() => loose = new SpikeItem { Flag = !loose.Flag }));
    }

    [Test]
    public async Task A_bool_or_enum_dependency_property_reads_without_boxing()
    {
        NoesisRuntime.Start();

        var host = new SpikeControl { Align = VerticalAlignment.Top, IsEnabled = false };

        var align = DependencyRead.Value(host, SpikeControl.AlignProperty);
        var enabled = DependencyRead.Value(host, UIElement.IsEnabledProperty);
        await Assert.That(align).IsEqualTo(host.GetValue(SpikeControl.AlignProperty));
        await Assert.That(enabled).IsEqualTo(host.GetValue(UIElement.IsEnabledProperty));

        await Assert
            .That(Allocated(() => _ = DependencyRead.Value(host, SpikeControl.AlignProperty)))
            .IsEqualTo(0);
        await Assert
            .That(Allocated(() => _ = DependencyRead.Value(host, UIElement.IsEnabledProperty)))
            .IsEqualTo(0);
        await Assert
            .That(DependencyRead.Value(host, SpikeControl.AlignProperty))
            .IsSameReferenceAs(align);

        host.Align = VerticalAlignment.Bottom;
        host.IsEnabled = true;
        await Assert
            .That(DependencyRead.Value(host, SpikeControl.AlignProperty))
            .IsEqualTo(VerticalAlignment.Bottom);
        await Assert.That(DependencyRead.Value(host, UIElement.IsEnabledProperty)).IsEqualTo(true);
    }

    [Test]
    public async Task Firing_a_watched_property_allocates_nothing_per_handler()
    {
        NoesisRuntime.Start();

        var gauge = new Gauge();
        NoesisRuntime.Show(gauge);
        var floor = Allocated(() => gauge.Level++);

        var seen = 0;
        DependencyWatcher.Watch(gauge, Gauge.LevelProperty, _ => seen += 1);
        DependencyWatcher.Watch(gauge, Gauge.LevelProperty, _ => seen += 10);
        DependencyWatcher.Watch(gauge, Gauge.LevelProperty, _ => seen += 100);

        var watched = Allocated(() => gauge.Level++);

        await Assert.That(watched).IsEqualTo(floor);
        await Assert.That(seen).IsEqualTo((Warmup + Iterations) * 111);
    }

    [Test]
    public async Task A_container_unloaded_and_loaded_again_keeps_one_subscription_per_binding()
    {
        NoesisRuntime.Start();

        var reads = 0;
        var host = new SpikeControl
        {
            DataContext = new SpikeItem { Label = "a", Index = 1 },
        };
        CompiledBinding.Bind(
            host,
            SpikeControl.LabelProperty,
            new CompiledBindingSpec
            {
                Hops =
                [
                    new BindingHop(
                        nameof(SpikeItem.Label),
                        o =>
                        {
                            reads++;
                            return ((SpikeItem)o).Label;
                        }
                    ),
                ],
            }
        );
        CompiledBinding.Bind(
            host,
            SpikeControl.CountProperty,
            new CompiledBindingSpec
            {
                Hops =
                [
                    new BindingHop(
                        nameof(SpikeItem.Index),
                        o =>
                        {
                            reads++;
                            return ((SpikeItem)o).Index;
                        }
                    ),
                ],
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, host);

        var before = reads;
        host.DataContext = new SpikeItem { Label = "b", Index = 2 };
        var perChange = reads - before;

        for (var cycle = 0; cycle < 4; cycle++)
        {
            root.Children.Remove(host);
            NoesisRuntime.Pump(view, root);
            root.Children.Add(host);
            NoesisRuntime.Pump(view, root);
        }

        before = reads;
        host.DataContext = new SpikeItem { Label = "c", Index = 3 };

        await Assert.That(host.Label).IsEqualTo("c");
        await Assert.That(host.Count).IsEqualTo(3);
        await Assert.That(reads - before).IsEqualTo(perChange);
    }

    [Test]
    public async Task A_settled_retry_stops_where_the_element_has_a_layout_handler_of_its_own()
    {
        NoesisRuntime.Start();

        var resolves = 0;
        var host = new SpikeControl();
        host.LayoutUpdated += (_, _) => { };
        CompiledBinding.Bind(
            host,
            SpikeControl.LabelProperty,
            new CompiledBindingSpec
            {
                Source = e =>
                {
                    resolves++;
                    return TreeSearch.Ancestor<StackPanel>(e);
                },
                Hops = [Hop.Label],
            }
        );

        var root = new StackPanel
        {
            Width = 400,
            Height = 300,
            DataContext = new SpikeItem { Label = "found" },
        };
        var view = NoesisRuntime.Show(root, host);
        await Assert.That(host.Label).IsEqualTo("found");

        var settled = resolves;
        for (var pass = 1; pass <= 4; pass++)
        {
            root.Width = 400 + pass;
            NoesisRuntime.Pump(view, root);
        }

        await Assert.That(resolves).IsEqualTo(settled);
    }
}
