using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledStyleScopeTests
{
    const string Xaml = """
        <ListBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 Width="400" Height="300">
          <ListBox.Template>
            <ControlTemplate TargetType="ListBox">
              <ItemsPresenter />
            </ControlTemplate>
          </ListBox.Template>
          <ListBox.ItemsPanel>
            <ItemsPanelTemplate>
              <StackPanel />
            </ItemsPanelTemplate>
          </ListBox.ItemsPanel>
        </ListBox>
        """;

    // A container style is applied by its target's type, so it lands on rows of another type too.
    [Test]
    public async Task A_typed_container_style_misses_on_a_row_of_another_type()
    {
        NoesisRuntime.Start();

        var compiled = Rows(
            (Style)
                XamlGenerated.NoesisToolkitEquivalenceTests.FixturesStMixedRowsxamlXaml.Build()[
                    "Row"
                ]
        );
        var parsed = Rows(
            (Style)((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/StMixedRows.xaml"))["Row"]
        );

        await Assert.That(parsed[0].Tag).IsEqualTo("model-name");
        await Assert.That(compiled[0].Tag).IsEqualTo(parsed[0].Tag);

        await Assert.That(parsed[1].Tag).IsNull();
        await Assert.That(compiled[1].Tag).IsEqualTo(parsed[1].Tag);
    }

    static ListBoxItem[] Rows(Style style)
    {
        var box = (ListBox)GUI.ParseXaml(Xaml);
        box.ItemsSource = new object[] { new StModel(), "plain" };
        box.ItemContainerStyle = style;
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, box);

        var generator = box.ItemContainerGenerator;
        return
        [
            (ListBoxItem)generator.ContainerFromIndex(0),
            (ListBoxItem)generator.ContainerFromIndex(1),
        ];
    }
}
