using System.Globalization;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public enum FmStage
{
    First,
    Second,
}

[Flags]
public enum FmFlags
{
    None = 0,
    A = 1,
    B = 2,
}

public sealed class FmNamed
{
    public override string ToString() => "named";
}

public sealed class FmUnnamed { }

public sealed class FmModel
{
    public static readonly DateTime Moment = new DateTime(2026, 1, 2, 3, 4, 5);

    public int Big { get; } = 1234567;
    public int Negative { get; } = -5;
    public double Pi { get; } = 1.5;
    public double Tie { get; } = 2.675;
    public double Half { get; } = 0.5;
    public float Quarter { get; } = 0.125f;
    public double Ratio { get; } = 1.0 / 3;
    public float Small { get; } = 0.1f;
    public double Infinite { get; } = double.PositiveInfinity;
    public double NegativeZero { get; } = -0.0;
    public sbyte Signed { get; } = -8;
    public bool Flag { get; } = true;
    public FmStage Stage { get; } = FmStage.Second;
    public FmFlags Combined { get; } = FmFlags.A | FmFlags.B;
    public int? Missing { get; }
    public string? Name { get; }
    public DateTime When { get; } = Moment;
    public FmNamed Named { get; } = new FmNamed();
    public char Letter { get; } = 'Q';
    public decimal Amount { get; } = 12.50m;
    public object Payload { get; } = new List<string> { "a" };
    public List<string> Items { get; } = new List<string> { "a" };
    public FmUnnamed Unnamed { get; } = new FmUnnamed();
}

[NotInParallel("Noesis")]
public sealed class CompiledFormattingParityTests
{
    [Test]
    [Arguments("en-CA")]
    [Arguments("de-DE")]
    [Arguments("sv-SE")]
    public async Task A_string_format_writes_what_the_native_engine_writes_in_any_culture(
        string culture
    )
    {
        NoesisRuntime.Start();
        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var (compiled, parsed) = Realize("Formats");

            var expected = new (string Name, string Text)[]
            {
                ("N0", "1,234,567"),
                ("NegativeN0", "-5"),
                ("F2", "1.50"),
                ("F2Tie", "2.68"),
                ("N0Half", "1"),
                ("P0", "13 %"),
                ("Plain", "0.3333333333333333%"),
                ("Level", "Level "),
                ("LevelMissing", ""),
                ("Aligned", "[    1.50]"),
                ("StageFormat", "Second"),
                ("WhenFormat", FmModel.Moment.ToString()),
                ("Joined", "1,234,567 / 2.68"),
            };

            foreach (var (name, text) in expected)
            {
                await Assert.That(Block(parsed, name).Text).IsEqualTo(text).Because(name);
                await Assert.That(Block(compiled, name).Text).IsEqualTo(text).Because(name);
                await Assert
                    .That(Bound(Block(compiled, name)))
                    .IsFalse()
                    .Because($"{name} fell back to a native binding");
            }

            foreach (var name in new[] { "Custom", "Hex" })
            {
                await Assert.That(Bound(Block(compiled, name))).IsTrue().Because(name);
                await Assert
                    .That(Block(compiled, name).Text)
                    .IsEqualTo(Block(parsed, name).Text)
                    .Because(name);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Test]
    public async Task A_format_without_braces_formats_the_one_value()
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize("Bare");

        foreach (var (name, text) in new[] { ("Bare", "1.50"), ("JoinedBare", "1,234,567") })
        {
            await Assert.That(Block(parsed, name).Text).IsEqualTo(text);
            await Assert.That(Block(compiled, name).Text).IsEqualTo(text);
            await Assert.That(Bound(Block(compiled, name))).IsFalse().Because(name);
        }
    }

    [Test]
    [Arguments("en-CA")]
    [Arguments("de-DE")]
    [Arguments("sv-SE")]
    public async Task A_value_reaches_a_text_slot_as_the_native_engine_shows_it(string culture)
    {
        NoesisRuntime.Start();
        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var (compiled, parsed) = Realize("Numbers");

            var expected = new (string Name, string Text)[]
            {
                ("Ratio", "0.3333333333333333"),
                ("Small", "0.1"),
                ("Negative", "-5"),
                ("Infinite", "∞"),
                ("NegativeZero", "0"),
                ("Flag", "True"),
                ("Stage", "Second"),
                ("Named", "named"),
                ("When", FmModel.Moment.ToString()),
                ("Missing", ""),
            };

            foreach (var (name, text) in expected)
            {
                await Assert.That(Block(parsed, name).Text).IsEqualTo(text).Because(name);
                await Assert.That(Block(compiled, name).Text).IsEqualTo(text).Because(name);
                await Assert.That(Bound(Block(compiled, name))).IsFalse().Because(name);
            }

            var parsedCombined = TreeSearch.Named<SpikeControl>(parsed, "Combined")!;
            var compiledCombined = TreeSearch.Named<SpikeControl>(compiled, "Combined")!;
            await Assert.That(parsedCombined.Label).IsEqualTo("host-default");
            await Assert.That(compiledCombined.Label).IsEqualTo("host-default");
            await Assert
                .That(
                    BindingOperations.GetBindingExpressionBase(
                        compiledCombined,
                        SpikeControl.LabelProperty
                    )
                )
                .IsNull();
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Test]
    public async Task A_char_or_decimal_source_keeps_its_native_binding()
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize("Unlike");

        foreach (var (name, text) in new[] { ("Letter", "81"), ("Amount", "12.5") })
        {
            await Assert.That(Block(parsed, name).Text).IsEqualTo(text);
            await Assert.That(Block(compiled, name).Text).IsEqualTo(text);
            await Assert.That(Bound(Block(compiled, name))).IsTrue().Because(name);
        }
    }

    [Test]
    public async Task A_source_whose_run_time_type_decides_its_text_keeps_its_native_binding()
    {
        NoesisRuntime.Start();

        var (compiled, parsed) = Realize("Unlike");

        foreach (var name in new[] { "Payload", "Items", "Unnamed" })
        {
            await Assert.That(Block(parsed, name).Text).IsEqualTo("").Because(name);
            await Assert.That(Block(compiled, name).Text).IsEqualTo("").Because(name);
            await Assert.That(Bound(Block(compiled, name))).IsTrue().Because(name);
        }
    }

    [Test]
    public async Task A_template_binding_writes_a_text_slot_only_a_string_or_null()
    {
        NoesisRuntime.Start();

        var (compiled, compiledHost, compiledView) = Templated(Compiled()["Chrome"]);
        var (parsed, parsedHost, parsedView) = Templated(Parsed()["Chrome"]);

        await Assert.That(Block(parsed, "TemplatedText").Text).IsEqualTo("");
        await Assert.That(Block(compiled, "TemplatedText").Text).IsEqualTo("");
        await Assert.That(Label(parsed, "TemplatedLabel")).IsEqualTo("host-default");
        await Assert.That(Label(compiled, "TemplatedLabel")).IsEqualTo("host-default");
        await Assert.That(Block(parsed, "TemplatedTag").Text).IsEqualTo("tagged");
        await Assert.That(Block(compiled, "TemplatedTag").Text).IsEqualTo("tagged");

        foreach (var name in new[] { "TemplatedText", "TemplatedTag" })
            await Assert.That(Bound(Block(compiled, name))).IsFalse().Because(name);

        // What it will not write leaves the last string in place, where a conversion would replace it.
        foreach (
            var (host, root, view) in new[]
            {
                (compiledHost, compiled, compiledView),
                (parsedHost, parsed, parsedView),
            }
        )
        {
            host.Tag = 9;
            NoesisRuntime.Pump(view, root);
        }

        await Assert.That(Block(parsed, "TemplatedTag").Text).IsEqualTo("tagged");
        await Assert.That(Block(compiled, "TemplatedTag").Text).IsEqualTo("tagged");

        foreach (
            var (host, root, view) in new[]
            {
                (compiledHost, compiled, compiledView),
                (parsedHost, parsed, parsedView),
            }
        )
        {
            host.Tag = null;
            NoesisRuntime.Pump(view, root);
        }

        await Assert.That(Block(parsed, "TemplatedTag").Text).IsEqualTo("");
        await Assert.That(Block(compiled, "TemplatedTag").Text).IsEqualTo("");

        var compiledWidth = TreeSearch.Named<Border>(compiled, "TemplatedWidth")!;
        await Assert.That(compiledWidth.Width).IsEqualTo(7f);
        await Assert
            .That(compiledWidth.Width)
            .IsEqualTo(TreeSearch.Named<Border>(parsed, "TemplatedWidth")!.Width);
        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    compiledWidth,
                    FrameworkElement.WidthProperty
                )
            )
            .IsNull();
    }

    [Test]
    public async Task A_number_formats_to_the_native_text_across_magnitudes_and_ties()
    {
        NoesisRuntime.Start();
        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        try
        {
            var random = new Random(7);
            var cases = new List<(object Value, string Format, string Mirrored)>();
            string[] floating =
            [
                "",
                "F0",
                "F2",
                "F3",
                "F",
                "N0",
                "N2",
                "n1",
                "P0",
                "P1",
                "p2",
                "F17",
            ];
            string[] integer = ["", "D", "D5", "N0", "n2", "F0", "f3", "P0", "p1", "X", "x8"];

            foreach (var value in Doubles(random))
            foreach (var format in floating)
                cases.Add(
                    (
                        value,
                        format,
                        format.Length == 0
                            ? NoesisToolkit.Mvvm.CodeGen.SlotConversion.Text(value)
                            : NoesisToolkit.Mvvm.CodeGen.SlotConversion.Text(value, format)
                    )
                );

            foreach (var value in Doubles(random).Select(d => (float)d))
            foreach (var format in floating)
                cases.Add(
                    (
                        value,
                        format,
                        format.Length == 0
                            ? NoesisToolkit.Mvvm.CodeGen.SlotConversion.Text(value)
                            : NoesisToolkit.Mvvm.CodeGen.SlotConversion.Text(value, format)
                    )
                );

            foreach (var value in Integers(random))
            foreach (var format in integer)
                cases.Add(
                    (
                        value,
                        format,
                        ((IFormattable)value).ToString(format, CultureInfo.InvariantCulture)
                    )
                );

            var panel = new StackPanel { Width = 400, Height = 400 };
            var blocks = new List<TextBlock>();
            foreach (var (value, format, _) in cases)
            {
                var block = new TextBlock { DataContext = new SpikeItem { Payload = value } };
                block.SetBinding(
                    TextBlock.TextProperty,
                    new Binding(nameof(SpikeItem.Payload))
                    {
                        StringFormat = format.Length == 0 ? "{0}" : "{0:" + format + "}",
                    }
                );
                panel.Children.Add(block);
                blocks.Add(block);
            }

            var view = GUI.CreateView(panel);
            view.SetSize(400, 400);
            NoesisRuntime.Pump(view, panel);

            var differing = cases
                .Select((c, i) => (c.Value, c.Format, c.Mirrored, Native: blocks[i].Text))
                .Where(c => c.Native != c.Mirrored)
                .Select(c =>
                    $"{c.Value.GetType().Name} {c.Value} [{c.Format}] native={c.Native} mirrored={c.Mirrored}"
                )
                .Take(20)
                .ToList();

            await Assert.That(differing).IsEmpty();
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    static IEnumerable<double> Doubles(Random random)
    {
        double[] edges =
        [
            0.5,
            -0.5,
            2.5,
            0.125,
            2.675,
            0.285,
            1.005,
            99.995,
            0.0,
            -0.0,
            -1e-7,
            -0.06,
            -0.04,
            -7e-5,
            1e15,
            1e16,
            1e17,
            1e21,
            1e-5,
            1e-4,
            123456789012345.6,
            double.MaxValue,
            double.NaN,
            double.PositiveInfinity,
            double.NegativeInfinity,
            1.0 / 3,
            0.1,
        ];
        foreach (var edge in edges)
            yield return edge;

        for (var i = 0; i < 400; i++)
        {
            var magnitude = Math.Pow(10, random.Next(-12, 18));
            var value =
                Math.Round(random.NextDouble() * 1000, random.Next(0, 6)) * magnitude / 1000;
            yield return random.Next(2) == 0 ? value : -value;
        }
    }

    static IEnumerable<object> Integers(Random random)
    {
        object[] edges =
        [
            int.MinValue,
            int.MaxValue,
            long.MinValue,
            long.MaxValue,
            ulong.MaxValue,
            uint.MaxValue,
            (short)-32768,
            (ushort)65535,
            (byte)255,
            (sbyte)127,
            0,
            -1,
        ];
        foreach (var edge in edges)
            yield return edge;

        for (var i = 0; i < 60; i++)
        {
            yield return random.Next(int.MinValue, int.MaxValue) >> random.Next(0, 31);
            yield return ((long)random.Next() << 32 | (uint)random.Next()) >> random.Next(0, 62);
            yield return (uint)random.Next() >> random.Next(0, 31);
            yield return (short)random.Next(short.MinValue, short.MaxValue);
            yield return (byte)random.Next(0, 255);
        }
    }

    static (Grid Compiled, Grid Parsed) Realize(string key) =>
        (Show((DataTemplate)Compiled()[key]), Show((DataTemplate)Parsed()[key]));

    static Grid Show(DataTemplate template)
    {
        var root = new Grid { Width = 400, Height = 2000 };
        NoesisRuntime.Show(
            root,
            new ContentControl { Content = new FmModel(), ContentTemplate = template }
        );
        return root;
    }

    static (Grid Root, SpikeControl Host, View View) Templated(object template)
    {
        var root = new Grid { Width = 400, Height = 300 };
        var host = new SpikeControl
        {
            Template = (ControlTemplate)template,
            Count = 7,
            Tag = "tagged",
        };
        return (root, host, NoesisRuntime.Show(root, host));
    }

    static string? Label(Grid root, string name) =>
        TreeSearch.Named<SpikeControl>(root, name)!.Label;

    static TextBlock Block(Grid root, string name) => TreeSearch.Named<TextBlock>(root, name)!;

    static bool Bound(TextBlock block) =>
        BindingOperations.GetBindingExpressionBase(block, TextBlock.TextProperty) is not null;

    static ResourceDictionary Compiled() =>
        XamlGenerated.NoesisToolkitEquivalenceTests.FixturesFmFormatsxamlXaml.Build();

    static ResourceDictionary Parsed() =>
        (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/FmFormats.xaml");
}
