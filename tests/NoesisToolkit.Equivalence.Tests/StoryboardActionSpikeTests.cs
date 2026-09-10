using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class StoryboardActionSpikeTests
{
    const string Markup = """
        <ResourceDictionary
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <ControlTemplate x:Key="Chrome" TargetType="{x:Type Button}">
            <ControlTemplate.Resources>
              <Storyboard x:Key="Grow">
                <DoubleAnimation Storyboard.TargetName="Bg" Storyboard.TargetProperty="Width"
                  To="120" Duration="0" FillBehavior="HoldEnd" />
              </Storyboard>
            </ControlTemplate.Resources>
            <Border x:Name="Bg" Width="40" />
            <ControlTemplate.Triggers>
              <DataTrigger Binding="{Binding Tag, RelativeSource={RelativeSource Self}}" Value="go">
                <DataTrigger.EnterActions>
                  <BeginStoryboard Storyboard="{StaticResource Grow}" />
                </DataTrigger.EnterActions>
              </DataTrigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </ResourceDictionary>
        """;

    static (Button Host, View View) Realized()
    {
        NoesisRuntime.Start();

        var dictionary = (ResourceDictionary)GUI.ParseXaml(Markup);
        var host = new Button { Template = (ControlTemplate)dictionary["Chrome"] };
        var view = NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, host);
        return (host, view);
    }

    [Test]
    public async Task A_native_enter_action_runs_its_storyboard()
    {
        var (host, view) = Realized();
        var border = TreeSearch.All<Border>(host).First();

        await Assert.That(border.Width).IsEqualTo(40f);

        host.Tag = "go";
        NoesisRuntime.Pump(view, host);

        await Assert.That(border.Width).IsEqualTo(120f);
    }
}
