using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// Judged on rendered value, not graph shape: a compiled binding leaves a local value where the
// parser leaves a BindingExpression, so shape diverges by construction.
[NotInParallel("Noesis")]
public sealed class CompiledRootTests
{
    [Test]
    public async Task A_compiled_root_renders_what_the_parsed_one_renders()
    {
        NoesisRuntime.Start();

        var compiledSource = new SpikeItem { Label = "first" };
        var compiled = new BoundFixture { DataContext = compiledSource };
        compiled.InitializeComponent();

        var parsedSource = new SpikeItem { Label = "first" };
        var parsed = (BoundFixture)GUI.LoadXaml("/Fixtures;Fixtures/Bound.xaml");
        parsed.DataContext = parsedSource;

        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, compiled, parsed);

        var compiledText = compiled.Compiled;
        var parsedText = (TextBlock)parsed.FindName("Compiled");

        await Assert.That(compiledText.Text).IsEqualTo(parsedText.Text);
        await Assert.That(compiledText.Text).IsEqualTo("first");

        compiledSource.Label = "second";
        parsedSource.Label = "second";
        await Assert.That(compiledText.Text).IsEqualTo(parsedText.Text);
        await Assert.That(compiledText.Text).IsEqualTo("second");

        // Without this the test would pass just as well on the fallback path.
        await Assert
            .That(BindingOperations.GetBindingExpression(parsedText, TextBlock.TextProperty))
            .IsNotNull();
        await Assert
            .That(BindingOperations.GetBindingExpression(compiledText, TextBlock.TextProperty))
            .IsNull();
    }
}
