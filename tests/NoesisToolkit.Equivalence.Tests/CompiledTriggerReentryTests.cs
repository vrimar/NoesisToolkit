using System.Globalization;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledTriggerReentryTests
{
    static (Grid Root, View View, SpikeItem Item) Realize(DataTemplate template)
    {
        var item = new SpikeItem();
        var host = new ContentControl { Content = item, ContentTemplate = template };
        var root = new Grid { Width = 400, Height = 300 };
        return (root, NoesisRuntime.Show(root, host), item);
    }

    static string State(Grid root, string name)
    {
        var border = TreeSearch.Named<Border>(root, name)!;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"tag={border.Tag ?? "-"} opacity={border.Opacity} width={border.Width}"
        );
    }

    [Test]
    [Arguments("Feedback", "tag=x opacity=0.5 width=NaN")]
    [Arguments("Chain", "tag=x opacity=0.5 width=33")]
    public async Task A_setter_that_feeds_a_condition_of_its_own_set_settles_as_natively(
        string name,
        string settled
    )
    {
        NoesisRuntime.Start();

        var parsed = (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/RtTriggerReentry.xaml");
        var native = Realize((DataTemplate)parsed["Row"]);
        var built =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesRtTriggerReentryxamlXaml.Build();
        var compiled = Realize((DataTemplate)built["Row"]);

        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.Named<Border>(compiled.Root, name)!).Count)
            .IsGreaterThan(0);
        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.Named<Border>(native.Root, name)!).Count)
            .IsEqualTo(0);

        native.Item.Flag = true;
        compiled.Item.Flag = true;
        NoesisRuntime.Pump(native.View, native.Root);
        NoesisRuntime.Pump(compiled.View, compiled.Root);

        await Assert.That(State(native.Root, name)).IsEqualTo(settled);
        await Assert.That(State(compiled.Root, name)).IsEqualTo(State(native.Root, name));

        native.Item.Flag = false;
        compiled.Item.Flag = false;
        NoesisRuntime.Pump(native.View, native.Root);
        NoesisRuntime.Pump(compiled.View, compiled.Root);

        await Assert.That(State(native.Root, name)).IsEqualTo("tag=- opacity=1 width=NaN");
        await Assert.That(State(compiled.Root, name)).IsEqualTo(State(native.Root, name));
    }

    // The native set overflows the stack here, so there is no native side to compare against.
    [Test]
    public async Task A_set_that_never_settles_stops_rather_than_looping()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem();
        var border = new Border { DataContext = item, Height = 5 };
        CompiledTriggerSet.Bind(
            border,
            [
                new CompiledTriggerSpec
                {
                    Conditions =
                    [
                        new CompiledTriggerCondition
                        {
                            Part = new CompiledBindingPart { Hops = [Hop.Flag] },
                            Value = true,
                        },
                        new CompiledTriggerCondition
                        {
                            Part = new CompiledBindingPart
                            {
                                SourceProperty = FrameworkElement.TagProperty,
                            },
                            Value = null,
                        },
                    ],
                    Setters =
                    [
                        new CompiledSetter { Property = FrameworkElement.TagProperty, Value = "x" },
                    ],
                },
            ]
        );

        var view = NoesisRuntime.Show(border);
        item.Flag = true;
        NoesisRuntime.Pump(view, border);

        await Assert.That(CompiledTriggerSet.SetsOf(border).Count).IsEqualTo(1);
    }
}
