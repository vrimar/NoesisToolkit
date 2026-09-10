using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// How a compiled binding is allowed to look an element up, pinned against what a native one resolves.
[NotInParallel("Noesis")]
public sealed class ElementSourceSpikeTests
{
    const string Markup = """
        <Grid
          xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
          xmlns:t="clr-namespace:NoesisToolkit.Equivalence.Tests;assembly=NoesisToolkit.Equivalence.Tests"
          Width="400"
          Height="300"
        >
          <ItemsControl x:Name="Host" Tag="host-tag">
            <ItemsControl.ItemTemplate>
              <DataTemplate>
                <StackPanel x:Name="Row">
                  <TextBlock x:Name="Peer" Text="{Binding Label}" />
                  <TextBlock
                    x:Name="Leaf"
                    t:SpikeProbeHook.Watch="1"
                    Text="{Binding ElementName=Peer, Path=Text}"
                    Tag="{Binding RelativeSource={RelativeSource AncestorType=ItemsControl}, Path=Tag}"
                  />
                </StackPanel>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
        </Grid>
        """;

    [Test]
    public async Task A_name_inside_a_template_resolves_off_the_clone_and_nowhere_else()
    {
        NoesisRuntime.Start();
        SpikeProbeHook.Seen.Clear();

        var (host, leaves) = Realize();
        var peers = leaves.Select(l => l.FindName("Peer")).Cast<TextBlock>().ToList();

        // What a native ElementName resolved, so FindName off the clone is the same lookup.
        await Assert.That(leaves.Select(l => l.Text)).IsEquivalentTo(["first", "second"]);
        await Assert.That(peers.Select(p => p.Text)).IsEquivalentTo(["first", "second"]);
        await Assert.That(peers[0]).IsNotSameReferenceAs(peers[1]);

        var prototype = host.ItemTemplate.FindName("Peer");
        await Assert.That(prototype).IsNotSameReferenceAs(peers[0]);
        await Assert.That(prototype).IsNotSameReferenceAs(peers[1]);
        await Assert.That(host.ItemTemplate.FindName("Peer", leaves[0])).IsNull();

        // A clone is wired before it is loaded, so a first frame off the real source needs this.
        await Assert
            .That(
                SpikeProbeHook.Seen.Any(s =>
                    s.StartsWith("at-callback", StringComparison.Ordinal)
                    && s.Contains("findName(Peer)=TextBlock", StringComparison.Ordinal)
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Only_the_visual_parent_chain_reaches_out_of_template_content()
    {
        NoesisRuntime.Start();
        SpikeProbeHook.Seen.Clear();

        var (host, leaves) = Realize();

        foreach (var leaf in leaves)
        {
            // What a native FindAncestor resolved.
            await Assert.That(leaf.Tag).IsEqualTo("host-tag");
            await Assert.That(TreeSearch.Ancestor<ItemsControl>(leaf)).IsSameReferenceAs(host);

            var logical = 0;
            for (FrameworkElement? p = leaf.Parent; p is not null; p = p.Parent)
                logical++;

            await Assert.That(leaf.Parent).IsTypeOf<StackPanel>();
            await Assert.That(logical).IsEqualTo(1);
        }
    }

    static (ItemsControl Host, List<TextBlock> Leaves) Realize()
    {
        var root = (Grid)GUI.ParseXaml(Markup);
        var host = (ItemsControl)root.FindName("Host");
        host.ItemsSource = new List<SpikeItem>
        {
            new() { Label = "first" },
            new() { Label = "second" },
        };

        var view = GUI.CreateView(root);
        view.SetSize(400, 300);
        for (var i = 0; i < 8; i++)
            view.Update(i * 0.016);
        root.UpdateLayout();

        var leaves = TreeSearch.All<TextBlock>(root).Where(t => t.Name == "Leaf").ToList();
        return (host, leaves);
    }
}
