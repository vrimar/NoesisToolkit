using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class TemplateSelfSourceSpikeTests
{
    const string Markup = """
        <ResourceDictionary
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ControlTemplate x:Key="Chrome" TargetType="{x:Type Button}">
            <Border x:Name="Bg" Tag="root" Background="Blue" />
            <ControlTemplate.Triggers>
              <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="parent">
                <Setter TargetName="Bg" Property="Background" Value="Green" />
              </DataTrigger>
              <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="root">
                <Setter TargetName="Bg" Property="Background" Value="Red" />
              </DataTrigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </ResourceDictionary>
        """;

    static Color Realized()
    {
        NoesisRuntime.Start();

        var dictionary = (ResourceDictionary)GUI.ParseXaml(Markup);
        var host = new Button { Template = (ControlTemplate)dictionary["Chrome"], Tag = "parent" };

        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, host);
        var border = TreeSearch.All<Border>(host).First();
        return ((SolidColorBrush)border.Background).Color;
    }

    [Test]
    public async Task Self_in_a_template_trigger_condition_is_the_templated_parent()
    {
        await Assert.That(Realized()).IsEqualTo(Colors.Green);
    }
}
