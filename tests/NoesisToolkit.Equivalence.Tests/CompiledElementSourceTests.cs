using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// Judged on rendered value, not graph shape: a compiled binding leaves a local value where the
// parser leaves a BindingExpression, so shape diverges by construction.
[NotInParallel("Noesis")]
public sealed class CompiledElementSourceTests
{
    [Test]
    public async Task An_attached_property_path_and_a_self_path_render_what_the_parsed_ones_render()
    {
        NoesisRuntime.Start();

        var compiled = new BoundElementFixture { DataContext = Owner() };
        compiled.InitializeComponent();

        var parsed = (BoundElementFixture)GUI.LoadXaml("/Fixtures;Fixtures/BoundElement.xaml");
        parsed.DataContext = Owner();

        var view = Host(compiled, parsed);
        NoesisRuntime.Pump(view, compiled, parsed);

        var compiledAttached = (TextBlock)compiled.FindName("FromAttached");
        var parsedAttached = (TextBlock)parsed.FindName("FromAttached");
        await Assert.That(compiledAttached.Text).IsEqualTo(parsedAttached.Text);
        await Assert.That(compiledAttached.Text).IsEqualTo("2");

        // "." names the source object, so both sides put their own DataContext in the slot.
        await Assert
            .That(((ContentControl)compiled.FindName("SelfContext")).Content)
            .IsSameReferenceAs(compiled.DataContext);
        await Assert
            .That(((ContentControl)parsed.FindName("SelfContext")).Content)
            .IsSameReferenceAs(parsed.DataContext);

        // Without this the test would pass just as well on the fallback path.
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(parsedAttached, TextBlock.TextProperty)
            )
            .IsNotNull();
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(compiledAttached, TextBlock.TextProperty)
            )
            .IsNull();
    }

    [Test]
    public async Task An_element_sourced_path_renders_what_the_parsed_one_renders()
    {
        NoesisRuntime.Start();

        var compiled = new BoundElementFixture { DataContext = Owner() };
        compiled.InitializeComponent();

        var parsed = (BoundElementFixture)GUI.LoadXaml("/Fixtures;Fixtures/BoundElement.xaml");
        parsed.DataContext = Owner();

        var view = Host(compiled, parsed);

        compiled.Anchor.DataContext = new SpikeItem { Label = "picked" };
        ((ContentControl)parsed.FindName("Anchor")).DataContext = new SpikeItem
        {
            Label = "picked",
        };
        NoesisRuntime.Pump(view, compiled, parsed);

        await Assert
            .That(compiled.FromElement.Text)
            .IsEqualTo(((TextBlock)parsed.FindName("FromElement")).Text);
        await Assert.That(compiled.FromElement.Text).IsEqualTo("picked");

        await Assert.That(Named(compiled, "FromPeer")).IsEquivalentTo(Named(parsed, "FromPeer"));
        await Assert.That(Named(compiled, "FromPeer")).IsEquivalentTo(["first", "second"]);

        await Assert
            .That(Named(compiled, "FromAncestor"))
            .IsEquivalentTo(Named(parsed, "FromAncestor"));
        await Assert.That(Named(compiled, "FromAncestor")).IsEquivalentTo(["owner", "owner"]);

        await Assert.That(Named(compiled, "FromOuter")).IsEquivalentTo(Named(parsed, "FromOuter"));
        await Assert.That(Named(compiled, "FromOuter")).IsEquivalentTo(["owner", "owner"]);

        await Assert.That(Named(compiled, "Inside")).IsEquivalentTo(Named(parsed, "Inside"));
        await Assert.That(Named(compiled, "Inside")).IsEquivalentTo(["from-parent"]);

        // Without this the test would pass just as well on the fallback path.
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    (TextBlock)parsed.FindName("FromElement"),
                    TextBlock.TextProperty
                )
            )
            .IsNotNull();
        // Only what this document named and meant to compile: a control's own template brings bound
        // chrome of its own, and Fallback is deliberately a mode the compiler refuses.
        foreach (
            var text in Blocks(compiled)
                .Where(t => !string.IsNullOrEmpty(t.Name) && t.Name != "Fallback")
        )
            await Assert
                .That(BindingOperations.GetBindingExpressionBase(text, TextBlock.TextProperty))
                .IsNull()
                .Because($"{text.Name} fell back to a native binding");
    }

    [Test]
    public async Task An_interface_slot_takes_what_implements_it()
    {
        NoesisRuntime.Start();

        var owner = Owner();
        var compiled = new BoundElementFixture { DataContext = owner };
        compiled.InitializeComponent();

        Host(compiled);

        var pokers = TreeSearch
            .All<Button>(compiled)
            .Where(b => b.Name is "Poker" or "Direct")
            .ToList();
        await Assert.That(pokers.Count).IsEqualTo(3);

        foreach (var poker in pokers)
        {
            await Assert
                .That(BindingOperations.GetBindingExpressionBase(poker, Button.CommandProperty))
                .IsNull()
                .Because("the command binding fell back to a native binding");

            await Assert.That(poker.Command).IsSameReferenceAs(owner.Poke);
            poker.Command.Execute(null);
        }

        await Assert.That(owner.Poke.Ran).IsEqualTo(3);
    }

    [Test]
    public async Task An_element_sourced_path_follows_its_source()
    {
        NoesisRuntime.Start();

        var owner = Owner();
        var compiled = new BoundElementFixture { DataContext = owner };
        compiled.InitializeComponent();

        var view = Host(compiled);
        var item = new SpikeItem { Label = "before" };
        compiled.Anchor.DataContext = item;
        NoesisRuntime.Pump(view, compiled);
        await Assert.That(compiled.FromElement.Text).IsEqualTo("before");

        item.Label = "after";
        await Assert.That(compiled.FromElement.Text).IsEqualTo("after");

        // The whole source element changing has to re-root the path, not just a hop off it.
        compiled.Anchor.DataContext = new SpikeItem { Label = "swapped" };
        await Assert.That(compiled.FromElement.Text).IsEqualTo("swapped");

        owner.Title = "renamed";
        await Assert.That(Named(compiled, "FromAncestor")).IsEquivalentTo(["renamed", "renamed"]);
    }

    static SpikeOwner Owner()
    {
        var owner = new SpikeOwner { Title = "owner" };
        owner.Items.Add(new SpikeItem { Label = "first" });
        owner.Items.Add(new SpikeItem { Label = "second" });
        return owner;
    }

    static string[] Named(FrameworkElement root, string name) =>
        Blocks(root).Where(t => t.Name == name).Select(t => t.Text).ToArray();

    static List<TextBlock> Blocks(DependencyObject node) => TreeSearch.All<TextBlock>(node);

    static View Host(params FrameworkElement[] elements) =>
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, elements);
}
