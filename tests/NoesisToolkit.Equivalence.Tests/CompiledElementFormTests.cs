using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public class EfChip : ContentControl
{
    public EfChip() { }

    public EfChip(string text) => Content = "ctor:" + text;
}

[NotInParallel("Noesis")]
public sealed class CompiledElementFormTests
{
    static ResourceDictionary Compiled() =>
        XamlGenerated.NoesisToolkitEquivalenceTests.FixturesEfElementFormsxamlXaml.Build();

    static ResourceDictionary Parsed() =>
        (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/EfElementForms.xaml");

    // The graph dump names a collection by type alone, so an empty one would compare equal.
    static string Items(ResourceDictionary dictionary)
    {
        var stops = ((LinearGradientBrush)dictionary["Stops"]).GradientStops;
        var figures = ((PathGeometry)dictionary["Figures"]).Figures;
        var items = ((ComboBox)dictionary["Listed"]).Items;
        return $"stops={stops.Count} figures={figures.Count} segments={figures[0].Segments.Count} items={items.Count}";
    }

    [Test]
    public async Task An_explicit_or_implied_collection_keeps_its_items()
    {
        NoesisRuntime.Start();

        await Assert.That(Items(Parsed())).IsEqualTo("stops=2 figures=1 segments=2 items=1");
        await Assert.That(Items(Compiled())).IsEqualTo(Items(Parsed()));
    }

    static Color Inked(ResourceDictionary dictionary)
    {
        var host = new ContentControl { Template = (ControlTemplate)dictionary["Inked"] };
        host.Resources = new ResourceDictionary();
        host.Resources["Ink"] = Colors.Red;
        var view = NoesisRuntime.Show(host);

        host.Resources["Ink"] = Colors.Blue;
        NoesisRuntime.Pump(view, host);
        var border = (Border)VisualTreeHelper.GetChild(host, 0);
        return ((SolidColorBrush)border.Background).Color;
    }

    [Test]
    public async Task A_resource_reference_on_a_brush_stays_live()
    {
        NoesisRuntime.Start();

        await Assert.That(Inked(Parsed())).IsEqualTo(Colors.Blue);
        await Assert.That(Inked(Compiled())).IsEqualTo(Colors.Blue);
    }
}
