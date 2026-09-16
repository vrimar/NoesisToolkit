using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledWhitespaceTests
{
    static ResourceDictionary Parsed() =>
        (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/WsInlines.xaml");

    static ResourceDictionary Compiled()
    {
        var compiled =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesWsInlinesxamlXaml.Build();
        XamlGenerated.NoesisToolkitEquivalenceTests.XamlResources.Flush(compiled);
        return compiled;
    }

    static T Realize<T>(ResourceDictionary dictionary, string key)
        where T : FrameworkElement
    {
        var host = new ContentControl
        {
            ContentTemplate = (DataTemplate)dictionary[key],
            Content = "c",
        };
        NoesisRuntime.Show(new StackPanel { Width = 800, Height = 300 }, host);
        return TreeSearch.All<T>(host).First();
    }

    static string Shape(InlineCollection inlines) =>
        string.Concat(
            inlines.Select(inline =>
                inline switch
                {
                    Run run => $"[{run.Text}]",
                    LineBreak => "<br>",
                    Span span => $"<{span.GetType().Name} {Shape(span.Inlines)}>",
                    _ => $"<{inline.GetType().Name}>",
                }
            )
        );

    static async Task AssertSameInlines(string key, string expected)
    {
        NoesisRuntime.Start();

        var parsed = Realize<TextBlock>(Parsed(), key);
        var compiled = Realize<TextBlock>(Compiled(), key);

        await Assert.That(Shape(parsed.Inlines)).IsEqualTo(expected);
        await Assert.That(Shape(compiled.Inlines)).IsEqualTo(Shape(parsed.Inlines));
        await Assert.That(compiled.ActualWidth).IsEqualTo(parsed.ActualWidth);
    }

    [Test]
    public async Task A_whitespace_gap_between_two_inline_elements_is_a_space_run() =>
        await AssertSameInlines("Ws.RunGap", "[5][ ][ Damage Reduced]");

    [Test]
    public async Task Text_beside_an_inline_element_keeps_that_edge_space() =>
        await AssertSameInlines(
            "Ws.RunTextRun",
            "[5 ][ SuperTortoiseGem(s) are required to upgrade to ][ -3 bless.]"
        );

    [Test]
    public async Task Text_after_a_line_break_trims_only_its_leading_space() =>
        await AssertSameInlines(
            "Ws.LineBreaks",
            "[Benefits: ]<br>[1. Stamina increased by 50 points. ]<br>[2. Revive wherever you are. ]"
        );

    [Test]
    public async Task Text_around_a_bold_keeps_the_space_that_touches_it() =>
        await AssertSameInlines("Ws.Mixed", "[Hello ]<Bold [World]>[ again]");

    [Test]
    public async Task A_span_keeps_edge_spaces_where_a_hyperlink_and_an_underline_trim_them() =>
        await AssertSameInlines(
            "Ws.Spans",
            "<Span [a ]<Bold [b]>[ c]>[ ]<Hyperlink [h][x]>[ ]<Underline [u][y][v]>"
        );

    [Test]
    public async Task Text_only_content_collapses_its_whitespace() =>
        await AssertSameInlines("Ws.TextOnly", "[Hello World again]");

    [Test]
    public async Task Preserved_space_is_kept_verbatim() =>
        await AssertSameInlines("Ws.Preserved", "[ t ][a][  ][b][ ]");

    [Test]
    public async Task Text_content_is_an_inline_a_style_text_setter_adds_to()
    {
        NoesisRuntime.Start();

        var parsed = Realize<TextBlock>(Parsed(), "Ws.Styled");
        var compiled = Realize<TextBlock>(Compiled(), "Ws.Styled");

        await Assert.That(Shape(parsed.Inlines)).IsEqualTo("[Styled][Default]");
        await Assert.That(Shape(compiled.Inlines)).IsEqualTo(Shape(parsed.Inlines));
        await Assert.That(compiled.ActualWidth).IsEqualTo(parsed.ActualWidth);
    }

    [Test]
    public async Task Content_and_property_element_text_collapse_their_whitespace()
    {
        NoesisRuntime.Start();

        var parsedDictionary = Parsed();
        var compiledDictionary = Compiled();
        var parsed = Realize<ContentControl>(parsedDictionary, "Ws.ContentText");
        var compiled = Realize<ContentControl>(compiledDictionary, "Ws.ContentText");

        await Assert.That(parsed.Content).IsEqualTo("Click me");
        await Assert.That(parsed.Tag).IsEqualTo("Tag text");
        await Assert.That(compiled.Content).IsEqualTo(parsed.Content);
        await Assert.That(compiled.Tag).IsEqualTo(parsed.Tag);

        await Assert.That(parsedDictionary["Ws.String"]).IsEqualTo("Hello World again");
        await Assert.That(compiledDictionary["Ws.String"]).IsEqualTo(parsedDictionary["Ws.String"]);
    }
}
