using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// A compiled trigger set judged against the native Style.Triggers it replaces: the compiled side
// keeps the style's ordinary setters native and strips only the triggers.
[NotInParallel("Noesis")]
public sealed class CompiledTriggerTests
{
    static CompiledTriggerCondition When(BindingHop hop, object value) =>
        new CompiledTriggerCondition
        {
            Part = new CompiledBindingPart { Hops = [hop] },
            Value = value,
        };

    static Style BaseStyle()
    {
        var style = new Style { TargetType = typeof(SpikeControl) };
        style.Setters.Add(new Setter { Property = SpikeControl.LabelProperty, Value = "base" });
        return style;
    }

    static Style NativeStyle()
    {
        var style = BaseStyle();

        var first = new DataTrigger { Binding = new Binding("Flag"), Value = "True" };
        first.Setters.Add(new Setter { Property = SpikeControl.LabelProperty, Value = "first" });
        style.Triggers.Add(first);

        var second = new DataTrigger { Binding = new Binding("Index"), Value = "1" };
        second.Setters.Add(new Setter { Property = SpikeControl.LabelProperty, Value = "second" });
        style.Triggers.Add(second);

        return style;
    }

    static CompiledTriggerSpec[] CompiledTriggers() =>
        [
            new CompiledTriggerSpec
            {
                Conditions = [When(Hop.Flag, true)],
                Setters =
                [
                    new CompiledSetter { Property = SpikeControl.LabelProperty, Value = "first" },
                ],
            },
            new CompiledTriggerSpec
            {
                Conditions = [When(Hop.Index, 1)],
                Setters =
                [
                    new CompiledSetter { Property = SpikeControl.LabelProperty, Value = "second" },
                ],
            },
        ];

    [Test]
    public async Task Order_activation_and_revert_land_where_the_native_triggers_land()
    {
        NoesisRuntime.Start();

        var nativeItem = new SpikeItem();
        var compiledItem = new SpikeItem();

        var nativeHost = new SpikeControl { DataContext = nativeItem, Style = NativeStyle() };
        var compiledHost = new SpikeControl { DataContext = compiledItem, Style = BaseStyle() };
        CompiledTriggerSet.Bind(compiledHost, CompiledTriggers());

        NoesisRuntime.Show(nativeHost, compiledHost);
        await Assert.That(compiledHost.Label).IsEqualTo("base");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Flag = true;
        compiledItem.Flag = true;
        await Assert.That(compiledHost.Label).IsEqualTo("first");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Index = 1;
        compiledItem.Index = 1;
        await Assert.That(compiledHost.Label).IsEqualTo("second");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Flag = false;
        compiledItem.Flag = false;
        await Assert.That(compiledHost.Label).IsEqualTo("second");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Index = 0;
        compiledItem.Index = 0;
        await Assert.That(compiledHost.Label).IsEqualTo("base");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);
    }

    [Test]
    public async Task An_element_carrying_a_style_set_and_a_template_set_keeps_both()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem();
        var host = new SpikeControl { DataContext = item, Style = BaseStyle() };

        CompiledTriggerSet.Bind(host, CompiledTriggers());
        CompiledTriggerSet.Bind(
            host,
            [
                new CompiledTriggerSpec
                {
                    Conditions =
                    [
                        new CompiledTriggerCondition
                        {
                            Part = new CompiledBindingPart
                            {
                                Hops =
                                [
                                    new BindingHop(
                                        nameof(SpikeItem.Flag),
                                        o => ((SpikeItem)o).Flag
                                    ),
                                ],
                            },
                            Value = true,
                        },
                    ],
                    Setters =
                    [
                        new CompiledSetter
                        {
                            Property = SpikeControl.AlignProperty,
                            Value = VerticalAlignment.Top,
                        },
                    ],
                },
            ]
        );

        NoesisRuntime.Show(host);
        await Assert.That(CompiledTriggerSet.SetsOf(host).Count).IsEqualTo(2);

        item.Flag = true;
        await Assert.That(host.Label).IsEqualTo("first");
        await Assert.That(host.Align).IsEqualTo(VerticalAlignment.Top);
    }

    [Test]
    public async Task A_broken_chain_holds_nothing_until_it_resolves()
    {
        NoesisRuntime.Start();

        var nativeStyle = BaseStyle();
        var nativeTrigger = new DataTrigger
        {
            Binding = new Binding("Payload.Flag"),
            Value = "True",
        };
        nativeTrigger.Setters.Add(
            new Setter { Property = SpikeControl.LabelProperty, Value = "hit" }
        );
        nativeStyle.Triggers.Add(nativeTrigger);

        var nativeItem = new SpikeItem();
        var compiledItem = new SpikeItem();

        var nativeHost = new SpikeControl { DataContext = nativeItem, Style = nativeStyle };
        var compiledHost = new SpikeControl { DataContext = compiledItem, Style = BaseStyle() };
        CompiledTriggerSet.Bind(
            compiledHost,
            [
                new CompiledTriggerSpec
                {
                    Conditions =
                    [
                        new CompiledTriggerCondition
                        {
                            Part = new CompiledBindingPart
                            {
                                Hops =
                                [
                                    new BindingHop(
                                        nameof(SpikeItem.Payload),
                                        o => ((SpikeItem)o).Payload
                                    ),
                                    BindingHop.Guarded<SpikeItem>(
                                        nameof(SpikeItem.Flag),
                                        s => s.Flag
                                    ),
                                ],
                            },
                            Value = true,
                        },
                    ],
                    Setters =
                    [
                        new CompiledSetter { Property = SpikeControl.LabelProperty, Value = "hit" },
                    ],
                },
            ]
        );

        NoesisRuntime.Show(nativeHost, compiledHost);
        await Assert.That(compiledHost.Label).IsEqualTo("base");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Payload = new SpikeItem { Flag = true };
        compiledItem.Payload = new SpikeItem { Flag = true };
        await Assert.That(compiledHost.Label).IsEqualTo("hit");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Payload = null;
        compiledItem.Payload = null;
        await Assert.That(compiledHost.Label).IsEqualTo("base");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);
    }

    [Test]
    public async Task Every_condition_of_a_multi_trigger_must_hold()
    {
        NoesisRuntime.Start();

        var nativeStyle = BaseStyle();
        var nativeTrigger = new MultiDataTrigger();
        nativeTrigger.Conditions.Add(
            new Condition { Binding = new Binding("Flag"), Value = "True" }
        );
        nativeTrigger.Conditions.Add(new Condition { Binding = new Binding("Index"), Value = "2" });
        nativeTrigger.Setters.Add(
            new Setter { Property = SpikeControl.LabelProperty, Value = "both" }
        );
        nativeStyle.Triggers.Add(nativeTrigger);

        var nativeItem = new SpikeItem();
        var compiledItem = new SpikeItem();

        var nativeHost = new SpikeControl { DataContext = nativeItem, Style = nativeStyle };
        var compiledHost = new SpikeControl { DataContext = compiledItem, Style = BaseStyle() };
        CompiledTriggerSet.Bind(
            compiledHost,
            [
                new CompiledTriggerSpec
                {
                    Conditions =
                    [
                        new CompiledTriggerCondition
                        {
                            Part = new CompiledBindingPart
                            {
                                Hops =
                                [
                                    new BindingHop(
                                        nameof(SpikeItem.Flag),
                                        o => ((SpikeItem)o).Flag
                                    ),
                                ],
                            },
                            Value = true,
                        },
                        new CompiledTriggerCondition
                        {
                            Part = new CompiledBindingPart
                            {
                                Hops =
                                [
                                    new BindingHop(
                                        nameof(SpikeItem.Index),
                                        o => ((SpikeItem)o).Index
                                    ),
                                ],
                            },
                            Value = 2,
                        },
                    ],
                    Setters =
                    [
                        new CompiledSetter
                        {
                            Property = SpikeControl.LabelProperty,
                            Value = "both",
                        },
                    ],
                },
            ]
        );

        NoesisRuntime.Show(nativeHost, compiledHost);

        nativeItem.Flag = true;
        compiledItem.Flag = true;
        await Assert.That(compiledHost.Label).IsEqualTo("base");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Index = 2;
        compiledItem.Index = 2;
        await Assert.That(compiledHost.Label).IsEqualTo("both");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        nativeItem.Flag = false;
        compiledItem.Flag = false;
        await Assert.That(compiledHost.Label).IsEqualTo("base");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);
    }

    [Test]
    public async Task An_enum_setter_lands_through_its_accessor()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem();
        var host = new SpikeControl { DataContext = item };
        CompiledTriggerSet.Bind(
            host,
            [
                new CompiledTriggerSpec
                {
                    Conditions =
                    [
                        new CompiledTriggerCondition
                        {
                            Part = new CompiledBindingPart
                            {
                                Hops =
                                [
                                    new BindingHop(
                                        nameof(SpikeItem.Flag),
                                        o => ((SpikeItem)o).Flag
                                    ),
                                ],
                            },
                            Value = true,
                        },
                    ],
                    Setters =
                    [
                        new CompiledSetter
                        {
                            Property = SpikeControl.AlignProperty,
                            Value = VerticalAlignment.Bottom,
                            Assign = (t, v) =>
                                ((SpikeControl)t).Align = v is VerticalAlignment a ? a : default,
                        },
                    ],
                },
            ]
        );

        NoesisRuntime.Show(host);
        await Assert.That(host.Align).IsEqualTo(VerticalAlignment.Center);

        item.Flag = true;
        await Assert.That(host.Align).IsEqualTo(VerticalAlignment.Bottom);

        item.Flag = false;
        await Assert.That(host.Align).IsEqualTo(VerticalAlignment.Center);
    }
}
