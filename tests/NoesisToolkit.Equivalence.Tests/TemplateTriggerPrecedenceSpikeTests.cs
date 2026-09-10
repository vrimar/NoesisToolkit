using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class TemplateTriggerPrecedenceSpikeTests
{
    const string Markup = """
        <ResourceDictionary
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ControlTemplate x:Key="Chrome" TargetType="{x:Type Button}">
            <Border x:Name="Bg" Background="Blue" />
            <ControlTemplate.Triggers>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Width" Value="50" />
                <Setter TargetName="Bg" Property="Background" Value="Green" />
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </ResourceDictionary>
        """;

    static Button Templated()
    {
        var dictionary = (ResourceDictionary)GUI.ParseXaml(Markup);
        return new Button { Template = (ControlTemplate)dictionary["Chrome"], IsEnabled = false };
    }

    [Test]
    public async Task A_local_value_on_the_control_outranks_a_template_trigger()
    {
        NoesisRuntime.Start();

        var host = Templated();
        host.Width = 200;
        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, host);

        await Assert.That(host.Width).IsEqualTo(200f);
    }

    [Test]
    public async Task Without_a_local_value_the_template_trigger_writes_the_templated_parent()
    {
        NoesisRuntime.Start();

        var host = Templated();
        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, host);

        await Assert.That(host.Width).IsEqualTo(50f);
    }

    [Test]
    public async Task A_template_attribute_does_not_outrank_a_trigger_targeting_it()
    {
        NoesisRuntime.Start();

        var host = Templated();
        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, host);

        var border = TreeSearch.All<Border>(host).First();
        await Assert.That(((SolidColorBrush)border.Background).Color).IsEqualTo(Colors.Green);
    }
}
