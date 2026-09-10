using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class DoubleLiteralSpikeTests
{
    const string Markup = """
        <ResourceDictionary
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          xmlns:sys="clr-namespace:System;assembly=mscorlib">
          <sys:Double x:Key="Gap">8</sys:Double>
        </ResourceDictionary>
        """;

    [Test]
    public async Task A_Double_resource_is_boxed_as_a_float_and_fills_a_float_slot()
    {
        NoesisRuntime.Start();

        var dictionary = (ResourceDictionary)GUI.ParseXaml(Markup);
        var gap = dictionary["Gap"];

        await Assert.That(gap).IsTypeOf<float>();
        await Assert.That(TextBlock.FontSizeProperty.PropertyType).IsEqualTo(typeof(float));

        var block = new TextBlock();
        block.SetValue(TextBlock.FontSizeProperty, gap);
        await Assert.That(block.FontSize).IsEqualTo(8f);
    }
}
