using System.ComponentModel;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public readonly struct SpTally
{
    public int Hits { get; init; }
    public double Rate { get; init; }
}

public sealed class SpTallyModel : INotifyPropertyChanged
{
    SpTally _tally;

    public SpTally Tally
    {
        get => _tally;
        set
        {
            _tally = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tally)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

[NotInParallel("Noesis")]
public sealed class CompiledStructPathTests
{
    [Test]
    public async Task A_member_read_through_a_struct_shows_and_follows_what_a_native_binding_does()
    {
        NoesisRuntime.Start();

        var compiledModel = new SpTallyModel
        {
            Tally = new SpTally { Hits = 3, Rate = 37.5 },
        };
        var parsedModel = new SpTallyModel
        {
            Tally = new SpTally { Hits = 3, Rate = 37.5 },
        };
        var compiled = Show(Compiled(), compiledModel);
        var parsed = Show(Parsed(), parsedModel);

        await Assert.That(Named(parsed, "Hits").Text).IsEqualTo("3");
        await AssertSame(compiled, parsed);

        compiledModel.Tally = new SpTally { Hits = 4, Rate = 50 };
        parsedModel.Tally = new SpTally { Hits = 4, Rate = 50 };

        await Assert.That(Named(parsed, "Hits").Text).IsEqualTo("4");
        await AssertSame(compiled, parsed);
    }

    static async Task AssertSame(ContentControl compiled, ContentControl parsed)
    {
        await Assert.That(Named(compiled, "Hits").Text).IsEqualTo(Named(parsed, "Hits").Text);
        await Assert.That(Named(compiled, "Rate").Text).IsEqualTo(Named(parsed, "Rate").Text);
        await Assert.That(Named(compiled, "Boxed").Tag).IsEqualTo(Named(parsed, "Boxed").Tag);
    }

    static TextBlock Named(ContentControl host, string name) =>
        TreeSearch.Named<TextBlock>(host, name)!;

    static ContentControl Show(DataTemplate template, SpTallyModel model)
    {
        var host = new ContentControl { Content = model, ContentTemplate = template };
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
        return host;
    }

    static DataTemplate Compiled() =>
        (DataTemplate)
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesSpStructPathsxamlXaml.Build()[
                "StructPaths"
            ];

    static DataTemplate Parsed() =>
        (DataTemplate)
            ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/SpStructPaths.xaml"))[
                "StructPaths"
            ];
}
