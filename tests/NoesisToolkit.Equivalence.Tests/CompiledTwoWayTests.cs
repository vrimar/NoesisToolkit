using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// Judged on rendered value, not graph shape: a compiled binding leaves a local value where the
// parser leaves a BindingExpression, so shape diverges by construction.
[NotInParallel("Noesis")]
public sealed class CompiledTwoWayTests
{
    [Test]
    public async Task A_two_way_path_carries_a_write_back_to_the_source()
    {
        NoesisRuntime.Start();

        var owner = new SpikeOwner { Title = "start" };
        var compiled = new BoundElementFixture { DataContext = owner };
        compiled.InitializeComponent();
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, compiled);

        await Assert.That(compiled.Entry.Text).IsEqualTo("start");

        compiled.Entry.Text = "typed";
        await Assert.That(owner.Title).IsEqualTo("typed");

        // The source still drives the target, so the write back has not replaced the read.
        owner.Title = "pushed";
        await Assert.That(compiled.Entry.Text).IsEqualTo("pushed");
    }

    [Test]
    public async Task A_property_that_binds_two_way_by_default_writes_back_unasked()
    {
        NoesisRuntime.Start();

        var owner = new SpikeOwner();
        var compiled = new BoundElementFixture { DataContext = owner };
        compiled.InitializeComponent();
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, compiled);

        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiled.Toggle,
                    ToggleButton.IsCheckedProperty
                )
            )
            .IsNull()
            .Because("IsChecked fell back to a native binding");

        await Assert.That(owner.Flag).IsFalse();
        compiled.Toggle.IsChecked = true;
        await Assert.That(owner.Flag).IsTrue();

        owner.Flag = false;
        await Assert.That(compiled.Toggle.IsChecked).IsEqualTo(false);
    }

    [Test]
    public async Task A_path_off_an_element_property_follows_that_property()
    {
        NoesisRuntime.Start();

        var owner = new SpikeOwner { Title = "start" };
        var compiled = new BoundElementFixture { DataContext = owner };
        compiled.InitializeComponent();
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, compiled);

        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(compiled.Mirror, TextBlock.TextProperty)
            )
            .IsNull()
            .Because("the element-property binding fell back to a native binding");

        await Assert.That(compiled.Mirror.Text).IsEqualTo("start");

        compiled.Entry.Text = "typed";
        await Assert.That(compiled.Mirror.Text).IsEqualTo("typed");
    }

    [Test]
    public async Task A_two_way_binding_renders_what_the_parsed_one_renders()
    {
        NoesisRuntime.Start();

        var compiledOwner = new SpikeOwner { Title = "start" };
        var compiled = new BoundElementFixture { DataContext = compiledOwner };
        compiled.InitializeComponent();

        var parsedOwner = new SpikeOwner { Title = "start" };
        var parsed = (BoundElementFixture)GUI.LoadXaml("/Fixtures;Fixtures/BoundElement.xaml");
        parsed.DataContext = parsedOwner;

        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, compiled, parsed);

        var parsedEntry = (TextBox)parsed.FindName("Entry");
        var parsedToggle = (CheckBox)parsed.FindName("Toggle");

        compiled.Entry.Text = "typed";
        parsedEntry.Text = "typed";
        await Assert.That(compiledOwner.Title).IsEqualTo(parsedOwner.Title);
        await Assert.That(compiledOwner.Title).IsEqualTo("typed");

        compiled.Toggle.IsChecked = true;
        parsedToggle.IsChecked = true;
        await Assert.That(compiledOwner.Flag).IsEqualTo(parsedOwner.Flag);
        await Assert.That(compiledOwner.Flag).IsTrue();
    }

    [Test]
    public async Task A_binding_that_fell_back_still_resolves_a_root_name()
    {
        NoesisRuntime.Start();

        var compiled = new BoundElementFixture { DataContext = new SpikeOwner { Title = "start" } };
        compiled.InitializeComponent();

        var parsed = (BoundElementFixture)GUI.LoadXaml("/Fixtures;Fixtures/BoundElement.xaml");
        parsed.DataContext = new SpikeOwner { Title = "start" };

        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, compiled, parsed);

        // OneTime is not compiled, so this is a real Noesis binding resolving ElementName itself.
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiled.Fallback,
                    TextBlock.TextProperty
                )
            )
            .IsNotNull();

        await Assert.That(compiled.FindName("Anchor")).IsSameReferenceAs(compiled.Anchor);
        await Assert
            .That(compiled.Fallback.Text)
            .IsEqualTo(((TextBlock)parsed.FindName("Fallback")).Text);
        await Assert.That(compiled.Fallback.Text).IsEqualTo("anchored");
    }
}
