using System.Collections.ObjectModel;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class ListRecyclingTests
{
    const string Xaml = """
        <ListBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 Width="400" Height="300"
                 VirtualizingStackPanel.IsVirtualizing="True"
                 VirtualizingStackPanel.VirtualizationMode="Recycling"
                 ScrollViewer.CanContentScroll="True">
          <ListBox.Template>
            <ControlTemplate TargetType="ListBox">
              <ScrollViewer CanContentScroll="True">
                <ScrollViewer.Template>
                  <ControlTemplate TargetType="ScrollViewer">
                    <ScrollContentPresenter x:Name="PART_ScrollContentPresenter"
                                            CanContentScroll="True" />
                  </ControlTemplate>
                </ScrollViewer.Template>
                <ItemsPresenter />
              </ScrollViewer>
            </ControlTemplate>
          </ListBox.Template>
          <ListBox.ItemsPanel>
            <ItemsPanelTemplate>
              <VirtualizingStackPanel />
            </ItemsPanelTemplate>
          </ListBox.ItemsPanel>
          <ListBox.ItemContainerStyle>
            <Style TargetType="ListBoxItem">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ListBoxItem">
                    <ContentPresenter />
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
          </ListBox.ItemContainerStyle>
          <ListBox.ItemTemplate>
            <DataTemplate>
              <TextBlock Text="{Binding}" Height="20" />
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
        """;

    static void Collect(DependencyObject node, HashSet<TextBlock> seen)
    {
        if (node is TextBlock text)
            seen.Add(text);

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            Collect(VisualTreeHelper.GetChild(node, i), seen);
    }

    static ScrollViewer? Scroller(DependencyObject node)
    {
        if (node is ScrollViewer viewer)
            return viewer;

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            if (Scroller(VisualTreeHelper.GetChild(node, i)) is { } found)
                return found;
        }

        return null;
    }

    [Test]
    public async Task A_list_stuck_to_the_bottom_reuses_its_containers_across_appends()
    {
        NoesisRuntime.Start();

        var rows = new ObservableCollection<string>();
        var box = (ListBox)GUI.ParseXaml(Xaml);
        box.ItemsSource = rows;

        var root = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, box);

        const int appended = 120;
        var seen = new HashSet<TextBlock>();
        for (var i = 0; i < appended; i++)
        {
            rows.Add($"row {i}");
            NoesisRuntime.Pump(view, root);
            Scroller(box)?.ScrollToBottom();
            NoesisRuntime.Pump(view, root);
            Collect(box, seen);
        }

        var visible = new HashSet<TextBlock>();
        Collect(box, visible);

        await Assert.That(visible.Count).IsGreaterThan(0);
        await Assert.That(visible.Count).IsLessThan(appended / 2);
        await Assert.That(seen.Count).IsLessThanOrEqualTo(visible.Count + 4);
    }
}
