using System.Collections.ObjectModel;
using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// Two items, because one clone showing the right text proves nothing about per-clone wiring.
[NotInParallel("Noesis")]
public sealed class CompiledTemplateTests
{
    [Test]
    public async Task Rebuilding_a_document_reuses_its_wiring_slots()
    {
        NoesisRuntime.Start();

        var first = CompiledBindingSetup.Register("probe#0", _ => { });

        // Hot reload runs every Build again; appending each time leaks a slot per reload.
        await Assert.That(CompiledBindingSetup.Register("probe#0", _ => { })).IsEqualTo(first);
        await Assert.That(CompiledBindingSetup.Register("probe#1", _ => { })).IsNotEqualTo(first);
    }

    [Test]
    public async Task Each_clone_binds_to_its_own_item()
    {
        NoesisRuntime.Start();

        var compiled =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesBoundTemplatexamlXaml.Build();
        var template = (DataTemplate)compiled["Row"];

        var items = new ObservableCollection<SpikeItem>
        {
            new() { Label = "first" },
            new() { Label = "second" },
        };

        var host = new ItemsControl { ItemsSource = items, ItemTemplate = template };
        var root = new Grid { Width = 400, Height = 300 };
        NoesisRuntime.Show(root, host);

        var texts = TreeSearch.All<TextBlock>(root);
        await Assert
            .That(texts.Select(t => t.Text).OrderBy(t => t, StringComparer.Ordinal).ToList())
            .IsEquivalentTo(["#0", "#0", "FIRST", "SECOND", "first", "second"]);

        foreach (var text in texts)
            await Assert
                .That(BindingOperations.GetBindingExpression(text, TextBlock.TextProperty))
                .IsNull();

        items[0].Label = "changed";
        root.UpdateLayout();
        await Assert
            .That(
                TreeSearch
                    .All<TextBlock>(root)
                    .Select(t => t.Text)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToList()
            )
            .IsEquivalentTo(["#0", "#0", "CHANGED", "SECOND", "changed", "second"]);
    }
}
