using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class PaHost : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        "IsOpen",
        typeof(bool),
        typeof(PaHost),
        new PropertyMetadata(false)
    );

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }
}

public sealed class PaModel
{
    public string Label { get; set; } = "row";

    public bool Open { get; set; }

    public List<PaRow> Rows { get; } = [new PaRow { Name = "a" }, new PaRow { Name = "b" }];
}

public sealed class PaRow
{
    public string Name { get; set; } = "";
}

[NotInParallel("Noesis")]
public sealed class CompiledPopupAncestorTests
{
    [Test]
    public async Task Content_of_an_open_popup_finds_the_control_around_it_as_the_native_binding_does()
    {
        NoesisRuntime.Start();

        foreach (var side in Sides(open: true))
        {
            await Assert.That(side.Direct.Tag).IsEqualTo("row").Because(side.Name);
            await Assert.That(side.Rows.Count).IsEqualTo(2).Because(side.Name);
            foreach (var row in side.Rows)
                await Assert.That(row.Tag).IsEqualTo("row").Because(side.Name);
        }
    }

    [Test]
    public async Task Content_of_a_closed_popup_finds_it_and_still_does_once_the_popup_opens()
    {
        NoesisRuntime.Start();

        foreach (var side in Sides(open: false))
        {
            await Assert.That(side.Direct.Tag).IsEqualTo("row").Because(side.Name);

            side.Host.IsOpen = true;
            side.Pump();

            await Assert.That(side.Direct.Tag).IsEqualTo("row").Because(side.Name);
            await Assert.That(side.Rows.Count).IsEqualTo(2).Because(side.Name);
            foreach (var row in side.Rows)
                await Assert.That(row.Tag).IsEqualTo("row").Because(side.Name);
        }
    }

    [Test]
    public async Task The_compiled_side_walks_in_code_and_the_parsed_side_natively()
    {
        NoesisRuntime.Start();

        var sides = Sides(open: true);
        await Assert.That(Native(sides[0].Direct)).IsFalse();
        await Assert.That(Native(sides[1].Direct)).IsTrue();
    }

    static bool Native(FrameworkElement element) =>
        BindingOperations.GetBindingExpressionBase(element, FrameworkElement.TagProperty)
            is not null;

    static Side[] Sides(bool open) =>
        [
            new Side(
                "compiled",
                XamlGenerated.NoesisToolkitEquivalenceTests.FixturesPaPopupxamlXaml.Build(),
                open
            ),
            new Side(
                "parsed",
                (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/PaPopup.xaml"),
                open
            ),
        ];

    sealed class Side
    {
        readonly Grid _root = new Grid { Width = 400, Height = 300 };
        readonly View _view;

        internal Side(string name, ResourceDictionary resources, bool open)
        {
            Name = name;
            _view = NoesisRuntime.Show(
                _root,
                new ContentControl
                {
                    Content = new PaModel { Open = open },
                    ContentTemplate = (DataTemplate)resources["Picker"],
                }
            );
        }

        internal string Name { get; }

        internal PaHost Host => TreeSearch.All<PaHost>(_root).Single();

        internal TextBlock Direct => (TextBlock)((StackPanel)Host.Content).Children[0];

        internal List<TextBlock> Rows =>
            TreeSearch
                .All<TextBlock>((StackPanel)Host.Content)
                .Where(t => !ReferenceEquals(t, Direct))
                .ToList();

        internal void Pump() => NoesisRuntime.Pump(_view, _root);
    }
}
