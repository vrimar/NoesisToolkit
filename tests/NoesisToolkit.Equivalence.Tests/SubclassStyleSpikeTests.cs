using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// Why a bound element cannot be given a generated subclass: an implicit style is keyed by the exact
// type, so the subclass loses it silently.
[NotInParallel("Noesis")]
public sealed class SubclassStyleSpikeTests
{
    const string Markup = """
        <ResourceDictionary
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          xmlns:t="clr-namespace:NoesisToolkit.Equivalence.Tests;assembly=NoesisToolkit.Equivalence.Tests">
          <Style TargetType="TextBlock">
            <Setter Property="Width" Value="123" />
          </Style>
        </ResourceDictionary>
        """;

    [Test]
    public async Task A_subclass_loses_the_implicit_style_of_its_base()
    {
        NoesisRuntime.Start();

        var styles = (ResourceDictionary)GUI.ParseXaml(Markup);

        var plain = new TextBlock();
        var derived = new BoundTextBlock();

        var root = new StackPanel
        {
            Width = 400,
            Height = 300,
            Resources = styles,
        };
        root.Children.Add(plain);
        root.Children.Add(derived);

        var view = GUI.CreateView(root);
        view.SetSize(400, 300);
        for (var i = 0; i < 6; i++)
            view.Update(i * 0.016);
        root.UpdateLayout();

        await Assert.That(plain.Width).IsEqualTo(123);
        await Assert.That(float.IsNaN(derived.Width)).IsTrue();
    }
}
