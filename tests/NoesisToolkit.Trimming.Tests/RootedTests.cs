using Noesis;

namespace NoesisToolkit.Trimming.Tests;

[NotInParallel("Noesis")]
public sealed class RootedTests
{
    static readonly Lazy<(Rooted View, PanelModel Model, View Host)> Realized = new(Realize);

    static (Rooted, PanelModel, View) Realize()
    {
        GUI.Init();

        var model = new PanelModel();
        var rooted = new Rooted
        {
            Width = 400,
            Height = 600,
            DataContext = model,
        };
        var host = GUI.CreateView(rooted);
        host.SetSize(400, 600);
        Pump(host, rooted);
        return (rooted, model, host);
    }

    // A template clone is wired a pass later than its host.
    static void Pump(View host, FrameworkElement root)
    {
        for (var i = 0; i < 8; i++)
            host.Update(i * 0.016);

        root.UpdateLayout();
    }

    static T Named<T>(string name)
        where T : FrameworkElement =>
        Descendants<T>(Realized.Value.View).Single(e => e.Name == name);

    static List<T> Descendants<T>(DependencyObject node)
        where T : DependencyObject
    {
        var found = new List<T>();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T hit)
                found.Add(hit);

            found.AddRange(Descendants<T>(child));
        }

        return found;
    }

    static string[] Texts(DependencyObject node) =>
        Descendants<TextBlock>(node).Select(t => t.Text).ToArray();

    [Test]
    public async Task A_native_binding_reads_each_hop()
    {
        await Assert.That(Named<TextBlock>("Hop").Text).IsEqualTo("hop");
    }

    [Test]
    public async Task A_hop_through_an_interface_reads_the_implementing_class()
    {
        await Assert.That(Named<TextBlock>("Through").Text).IsEqualTo("through");
    }

    [Test]
    public async Task A_DisplayMemberPath_reads_each_item()
    {
        await Assert
            .That(Texts(Named<ItemsControl>("Display")))
            .IsEquivalentTo(["first", "second"]);
    }

    [Test]
    public async Task An_ancestor_walk_past_a_template_reads_the_stated_type()
    {
        await Assert
            .That(Texts(Named<ItemsControl>("Walk")))
            .IsEquivalentTo(["ancestor", "ancestor"]);
    }

    [Test]
    public async Task A_generated_command_is_bound_and_runs()
    {
        var (_, model, _) = Realized.Value;
        var command = Named<Button>("Act").Command;

        await Assert.That(command).IsNotNull();
        command!.Execute(null);
        await Assert.That(model.Saves).IsEqualTo(1);
    }

    [Test]
    public async Task Items_a_template_reads_through_a_plain_property_keep_their_value_path()
    {
        var choices = Named<ListBox>("Choices");
        choices.SelectedIndex = 1;

        await Assert.That(choices.SelectedValue).IsEqualTo("female");
    }

    [Test]
    public async Task An_EventName_subscribes_to_the_event_it_names()
    {
        OnEvent.Fired = 0;
        Named<Pinger>("Pinger").Raise();

        await Assert.That(OnEvent.Fired).IsEqualTo(1);
    }
}
