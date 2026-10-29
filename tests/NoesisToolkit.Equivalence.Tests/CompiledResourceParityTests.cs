using Noesis;
using Generated = XamlGenerated.NoesisToolkitEquivalenceTests;

namespace NoesisToolkit.Equivalence.Tests;

public partial class ResBareRoot : UserControl { }

public partial class ResKeyedWrapperRoot : UserControl { }

public partial class ResLaterRoot : UserControl { }

public partial class ResEscapedOrderRoot : UserControl { }

public partial class ResLiveRoot : UserControl { }

public sealed class ResThemeDictionary : ResourceDictionary
{
    public string Mode { get; set; } = "Light";
}

public sealed class ResContext
{
    public string Name { get; set; } = "context-name";
}

/// <summary>Logs each change callback with the state the element is in when it fires.</summary>
public sealed class ResRecorder : ContentControl
{
    public static readonly List<string> Log = new();

    public static readonly DependencyProperty AlphaProperty = DependencyProperty.Register(
        "Alpha",
        typeof(string),
        typeof(ResRecorder),
        new PropertyMetadata(null, (d, e) => ((ResRecorder)d).Record("Alpha", e))
    );

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        "Value",
        typeof(string),
        typeof(ResRecorder),
        new PropertyMetadata(null, (d, e) => ((ResRecorder)d).Record("Value", e))
    );

    public string? Alpha
    {
        get => (string?)GetValue(AlphaProperty);
        set => SetValue(AlphaProperty, value);
    }

    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    void Record(string name, DependencyPropertyChangedEventArgs e) =>
        Log.Add($"{name}={e.NewValue} alpha={Alpha ?? "null"} styled={Style is not null}");
}

[NotInParallel("Noesis")]
public sealed class CompiledResourceParityTests
{
    static ResourceDictionary Parsed(string file) =>
        (ResourceDictionary)GUI.LoadXaml($"/Fixtures;Fixtures/{file}");

    static string? ColorOf(object? brush) => (brush as SolidColorBrush)?.Color.ToString();

    static Thickness SetterValue(object style) =>
        (Thickness)((Setter)((Style)style).Setters[0]).Value;

    [Test]
    public async Task A_dictionary_root_keeps_its_own_type_and_attributes()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResThemedRoot.xaml");
        var compiled = Generated.FixturesResThemedRootxamlXaml.Build();

        foreach (var (dictionary, side) in new[] { (parsed, "parsed"), (compiled, "compiled") })
            await Assert
                .That((dictionary as ResThemeDictionary)?.Mode)
                .IsEqualTo("Dark")
                .Because(side);
    }

    [Test]
    public async Task A_dictionary_root_with_a_source_holds_what_the_source_declares()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResSourcedRoot.xaml");
        var compiled = Generated.FixturesResSourcedRootxamlXaml.Build();

        foreach (var (dictionary, side) in new[] { (parsed, "parsed"), (compiled, "compiled") })
            await Assert.That(dictionary.Contains("Accent")).IsTrue().Because(side);
    }

    [Test]
    public async Task A_sourced_dictionary_keeps_the_entries_written_inside_it()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResSourceInline.xaml");
        var compiled = Generated.FixturesResSourceInlinexamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        foreach (var (dictionary, side) in new[] { (parsed, "parsed"), (compiled, "compiled") })
        {
            var resources = ((Border)dictionary["Res.SourceHost"]).Resources;

            await Assert.That(resources.Count).IsEqualTo(5).Because(side);
            await Assert.That(resources.Contains("Res.Inline")).IsTrue().Because(side);
            await Assert.That(resources.Contains("Muted")).IsTrue().Because(side);
            await Assert.That(ColorOf(resources["Accent"])).IsEqualTo("#FF3366CC").Because(side);
            await Assert
                .That(ColorOf(((Border)resources["Res.UsesAccent"]).Background))
                .IsEqualTo("#FF3366CC")
                .Because(side);
            await Assert.That(dictionary.Contains("Res.MergedInline")).IsTrue().Because(side);
        }
    }

    [Test]
    public async Task Unwrapped_resources_join_the_dictionary_the_element_already_holds()
    {
        NoesisRuntime.Start();

        var parsed = new ResBareRoot { Resources = new ResourceDictionary() };
        parsed.Resources["Res.Pre"] = "pre";
        var parsedBefore = parsed.Resources;
        GUI.LoadComponent(parsed, "/Fixtures;Fixtures/ResBareRoot.xaml");

        var compiled = new ResBareRoot { Resources = new ResourceDictionary() };
        compiled.Resources["Res.Pre"] = "pre";
        var compiledBefore = compiled.Resources;
        compiled.InitializeComponent();

        await Assert.That(parsed.Resources == parsedBefore).IsTrue();
        await Assert.That(parsed.Resources.Contains("Res.Pre")).IsTrue();
        await Assert.That(parsed.Resources.Count).IsEqualTo(2);

        await Assert.That(compiled.Resources == compiledBefore).IsTrue();
        await Assert.That(compiled.Resources.Contains("Res.Pre")).IsTrue();
        await Assert.That(compiled.Resources.Contains("Res.Bare")).IsTrue();
        await Assert.That(compiled.Resources.Count).IsEqualTo(parsed.Resources.Count);

        var parsedFirst = parsed.Resources["Res.Bare"];
        GUI.LoadComponent(parsed, "/Fixtures;Fixtures/ResBareRoot.xaml");
        var compiledFirst = compiled.Resources["Res.Bare"];
        compiled.InitializeComponent();

        await Assert.That(parsed.Resources["Res.Bare"] == parsedFirst).IsTrue();
        await Assert.That(compiled.Resources["Res.Bare"] == compiledFirst).IsTrue();
        await Assert.That(compiled.Resources.Count).IsEqualTo(parsed.Resources.Count);
    }

    [Test]
    public async Task A_keyed_dictionary_as_the_only_resource_is_dropped_and_leaves_the_element_as_it_was()
    {
        NoesisRuntime.Start();

        var parsed = new ResKeyedWrapperRoot { Resources = new ResourceDictionary() };
        parsed.Resources["Res.Pre"] = "pre";
        GUI.LoadComponent(parsed, "/Fixtures;Fixtures/ResKeyedWrapperRoot.xaml");

        var compiled = new ResKeyedWrapperRoot { Resources = new ResourceDictionary() };
        compiled.Resources["Res.Pre"] = "pre";
        compiled.InitializeComponent();

        await Assert.That(parsed.Resources.Count).IsEqualTo(1);
        await Assert.That(parsed.Resources.Contains("Res.InNested")).IsFalse();
        await Assert.That(compiled.Resources.Count).IsEqualTo(parsed.Resources.Count);
        await Assert.That(compiled.Resources.Contains("Res.Pre")).IsTrue();
        await Assert.That(compiled.Resources.Contains("Res.InNested")).IsFalse();
        await Assert.That(compiled.Resources.Contains("Res.Nested")).IsFalse();
    }

    [Test]
    public async Task A_key_its_own_dictionary_declares_further_down_is_not_found()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResForward.xaml");
        var compiled = Generated.FixturesResForwardxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        foreach (var (dictionary, side) in new[] { (parsed, "parsed"), (compiled, "compiled") })
        {
            await Assert.That(((Style)dictionary["Res.Forward"]).BasedOn).IsNull().Because(side);
            await Assert
                .That(((TextBlock)dictionary["Res.ForwardUse"]).Background)
                .IsNull()
                .Because(side);
            await Assert
                .That(ColorOf(((TextBlock)dictionary["Res.ForwardApp"]).Background))
                .IsEqualTo("#80FFFFFF")
                .Because(side);
            await Assert
                .That(ColorOf(((TextBlock)dictionary["Res.BackwardUse"]).Background))
                .IsEqualTo("#FF123456")
                .Because(side);
        }
    }

    [Test]
    public async Task Resources_after_content_are_not_in_scope_for_that_content()
    {
        NoesisRuntime.Start();

        var parsed = new ResLaterRoot();
        GUI.LoadComponent(parsed, "/Fixtures;Fixtures/ResLaterRoot.xaml");
        var compiled = new ResLaterRoot();
        compiled.InitializeComponent();

        string? Background(FrameworkElement root, string name) =>
            ColorOf(((Border)root.FindName(name)).Background);

        string? TagBackground(FrameworkElement root) =>
            ColorOf(((Border)((StackPanel)root.FindName("EndHost")).Tag).Background);

        await Assert.That(Background(parsed, "EndBefore")).IsNull();
        await Assert.That(Background(parsed, "MiddleBefore")).IsNull();
        await Assert.That(Background(parsed, "MiddleAfter")).IsNull();
        await Assert.That(Background(parsed, "StartAfter")).IsEqualTo("#FFABCDEF");
        await Assert.That(TagBackground(parsed)).IsEqualTo("#FFABCDEF");

        foreach (var name in new[] { "EndBefore", "MiddleBefore", "MiddleAfter", "StartAfter" })
            await Assert
                .That(Background(compiled, name))
                .IsEqualTo(Background(parsed, name))
                .Because(name);

        await Assert.That(TagBackground(compiled)).IsEqualTo(TagBackground(parsed));
    }

    [Test]
    public async Task A_type_key_uses_the_name_the_parser_keys_that_type_by()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResKeys.xaml");
        var compiled = Generated.FixturesResKeysxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        async Task<(float Button, float Check, string Text, string Flag, string Letter)> Realize(
            ResourceDictionary dictionary
        )
        {
            var button = new Button();
            var check = new CheckBox();
            var text = new ContentControl { Content = "x" };
            var flag = new ContentControl { Content = true };
            var letter = new ContentControl { Content = 'q' };
            NoesisRuntime.Show(
                new StackPanel
                {
                    Width = 400,
                    Height = 300,
                    Resources = dictionary,
                },
                button,
                check,
                text,
                flag,
                letter
            );

            await Assert.That(((Style)dictionary["Res.CheckDerived"]).BasedOn).IsNotNull();

            static string Shown(ContentControl host) =>
                string.Join("/", TreeSearch.All<TextBlock>(host).Select(t => t.Text));

            return (button.ActualWidth, check.ActualWidth, Shown(text), Shown(flag), Shown(letter));
        }

        var expected = await Realize(parsed);
        var actual = await Realize(compiled);

        await Assert.That(expected.Button).IsEqualTo(33f);
        await Assert.That(expected.Check).IsEqualTo(55f);
        await Assert.That(expected.Text).IsEqualTo("res-string");
        await Assert.That(actual).IsEqualTo(expected);

        foreach (var key in new[] { "Button", "CheckBox", "String", "Bool" })
            await Assert.That(compiled.Contains(key)).IsEqualTo(parsed.Contains(key)).Because(key);
    }

    [Test]
    public async Task Setter_value_text_converts_against_the_property_it_sets()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResSetterText.xaml");
        var compiled = Generated.FixturesResSetterTextxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        var parsedBorder = new Border { Style = (Style)parsed["Res.SetterText"] };
        var compiledBorder = new Border { Style = (Style)compiled["Res.SetterText"] };
        NoesisRuntime.Show(parsedBorder, compiledBorder);

        await Assert.That(ColorOf(parsedBorder.Background)).IsEqualTo("#FF00FF00");
        await Assert.That(parsedBorder.Width).IsEqualTo(42f);
        await Assert.That(parsedBorder.Tag).IsEqualTo("two words");

        await Assert
            .That(ColorOf(compiledBorder.Background))
            .IsEqualTo(ColorOf(parsedBorder.Background));
        await Assert.That(compiledBorder.Width).IsEqualTo(parsedBorder.Width);
        await Assert.That(compiledBorder.Tag).IsEqualTo(parsedBorder.Tag);

        var parsedTrigger = ((Trigger)((Style)parsed["Res.SetterText"]).Triggers[0]).Value;
        var compiledTrigger = ((Trigger)((Style)compiled["Res.SetterText"]).Triggers[0]).Value;
        await Assert.That(parsedTrigger).IsEqualTo(12f);
        await Assert.That(compiledTrigger).IsEqualTo(parsedTrigger);
    }

    [Test]
    public async Task A_dictionarys_own_entry_outranks_a_merged_one_under_an_unmanaged_key()
    {
        NoesisRuntime.Start();

        var parsed = new ResEscapedOrderRoot();
        GUI.LoadComponent(parsed, "/Fixtures;Fixtures/ResEscapedOrderRoot.xaml");
        var compiled = new ResEscapedOrderRoot();
        compiled.InitializeComponent();
        NoesisRuntime.Show(parsed, compiled);

        var expected = ((Button)parsed.FindName("ToolButton")).ActualWidth;
        await Assert.That(expected).IsEqualTo(44f);
        await Assert.That(compiled.ToolButton.ActualWidth).IsEqualTo(expected);
    }

    [Test]
    public async Task A_source_the_registry_cannot_name_loads_against_the_document_that_wrote_it()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("Styles.xaml");

        var entries = Generated.XamlRegistry.Dictionaries;
        var index = Array.FindIndex(entries, e => e.Logical == "Fixtures;Fixtures/Palette.xaml");
        var original = entries[index];
        entries[index] = (original.Path, "unregistered", original.Build);
        try
        {
            var compiled = Generated.FixturesStylesxamlXaml.Build();

            await Assert.That(parsed.MergedDictionaries[0].Contains("Accent")).IsTrue();
            await Assert
                .That(compiled.MergedDictionaries[0].Contains("Accent"))
                .IsEqualTo(parsed.MergedDictionaries[0].Contains("Accent"));
        }
        finally
        {
            entries[index] = original;
        }
    }

    [Test]
    public async Task A_rooted_source_without_an_assembly_is_relative_to_the_project()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResSub/ResRooted.xaml");
        var compiled = Generated.FixturesResSubResRootedxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        await Assert.That(ColorOf(parsed["Res.Rooted"])).IsEqualTo("#FF3366CC");
        await Assert.That(ColorOf(compiled["Res.Rooted"])).IsEqualTo(ColorOf(parsed["Res.Rooted"]));
    }

    [Test]
    public async Task A_struct_member_read_from_a_resource_is_kept_in_the_dictionary()
    {
        NoesisRuntime.Start();

        var original = GUI.GetApplicationResources();
        try
        {
            var graph = new ResourceDictionary();
            graph.MergedDictionaries.Add(original);
            graph["Res.OutsideLeft"] = 5f;
            GUI.SetApplicationResources(graph);

            var parsed = Parsed("ResStruct.xaml");
            var compiled = Generated.FixturesResStructxamlXaml.Build();
            Generated.XamlResources.Flush(graph);

            var parsedPad = (Thickness)parsed["Res.Pad"];
            var compiledPad = (Thickness)compiled["Res.Pad"];
            await Assert.That(parsedPad.Left).IsEqualTo(7f);
            await Assert.That(compiledPad).IsEqualTo(parsedPad);

            var parsedOutside = (Thickness)parsed["Res.PadOutside"];
            var compiledOutside = (Thickness)compiled["Res.PadOutside"];
            await Assert.That(parsedOutside.Left).IsEqualTo(5f);
            await Assert.That(compiledOutside).IsEqualTo(parsedOutside);

            var parsedSetter = SetterValue(parsed["Res.PadSetter"]);
            var compiledSetter = SetterValue(compiled["Res.PadSetter"]);
            await Assert.That(parsedSetter.Left).IsEqualTo(5f);
            await Assert.That(compiledSetter).IsEqualTo(parsedSetter);
        }
        finally
        {
            GUI.SetApplicationResources(original);
        }
    }

    [Test]
    public async Task A_keyed_element_gets_its_style_before_its_other_values_settle()
    {
        NoesisRuntime.Start();

        ResRecorder.Log.Clear();
        var parsed = (ResRecorder)Parsed("ResStyleOrder.xaml")["Res.Recorder"];
        var expected = ResRecorder.Log.ToList();

        ResRecorder.Log.Clear();
        var compiledDictionary = Generated.FixturesResStyleOrderxamlXaml.Build();
        var compiled = (ResRecorder)compiledDictionary["Res.Recorder"];
        Generated.XamlResources.Flush(compiledDictionary);
        var actual = ResRecorder.Log.ToList();

        await Assert.That(expected).IsNotEmpty();
        await Assert.That(parsed.Alpha).IsEqualTo("styled");
        await Assert
            .That(actual)
            .IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(compiled.Alpha).IsEqualTo(parsed.Alpha);
    }

    [Test]
    public async Task A_dictionary_built_after_a_reload_resolves_against_the_graph_installed_now()
    {
        NoesisRuntime.Start();

        var original = GUI.GetApplicationResources();
        try
        {
            var stale = new ResourceDictionary();
            stale["Res.ExternalBrush"] = new SolidColorBrush(Color.FromArgb(255, 0, 0, 255));
            Generated.XamlResources.Flush(stale);

            var reloaded = new ResourceDictionary();
            reloaded.MergedDictionaries.Add(original);
            reloaded["Res.ExternalBrush"] = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
            GUI.SetApplicationResources(reloaded);

            var parsed = Parsed("ResExternal.xaml");
            var compiled = Generated.FixturesResExternalxamlXaml.Build();

            string? SetterColor(ResourceDictionary dictionary) =>
                ColorOf(((Setter)((Style)dictionary["Res.External"]).Setters[0]).Value);

            await Assert.That(SetterColor(parsed)).IsEqualTo("#FFFF0000");
            await Assert.That(SetterColor(compiled)).IsEqualTo(SetterColor(parsed));
        }
        finally
        {
            GUI.SetApplicationResources(original);
        }
    }

    [Test]
    public async Task A_template_applies_a_data_context_read_from_its_own_dictionary()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResDataContext.xaml");
        var compiled = Generated.FixturesResDataContextxamlXaml.Build();

        (object? Context, string Text) Realize(ResourceDictionary dictionary)
        {
            var host = new ContentControl
            {
                ContentTemplate = (DataTemplate)dictionary["Res.ContextTemplate"],
                Content = "outer",
            };
            NoesisRuntime.Show(host);
            var panel = TreeSearch.All<StackPanel>(host).First();
            return (panel.DataContext, TreeSearch.All<TextBlock>(panel).First().Text);
        }

        var expected = Realize(parsed);
        var actual = Realize(compiled);

        await Assert.That(expected.Context).IsTypeOf<ResContext>();
        await Assert.That(expected.Text).IsEqualTo("context-name");
        await Assert.That(actual.Context).IsSameReferenceAs(compiled["Res.Context"]);
        await Assert.That(actual.Text).IsEqualTo(expected.Text);
    }

    [Test]
    public async Task A_resource_of_another_type_leaves_the_slot_as_it_was()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResMistypedEntry.xaml");
        var compiled = Generated.FixturesResMistypedEntryxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        static object Local(ResourceDictionary dictionary) =>
            ((Border)dictionary["Res.MistypedHost"]).ReadLocalValue(Border.BackgroundProperty);

        await Assert.That(Local(parsed)).IsSameReferenceAs(DependencyProperty.UnsetValue);
        await Assert.That(Local(compiled)).IsSameReferenceAs(DependencyProperty.UnsetValue);
    }

    [Test]
    public async Task A_resource_key_given_by_name_resolves_as_a_positional_one_does()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResNamedKey.xaml");
        var compiled = Generated.FixturesResNamedKeyxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        static string? Fill(ResourceDictionary dictionary) =>
            ColorOf(((Border)dictionary["Res.NamedKeyHost"]).Background);

        await Assert.That(Fill(parsed)).IsEqualTo("#FF3366CC");
        await Assert.That(Fill(compiled)).IsEqualTo(Fill(parsed));
    }

    [Test]
    public async Task An_empty_string_entry_is_the_empty_string()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResEmptyString.xaml");
        var compiled = Generated.FixturesResEmptyStringxamlXaml.Build();

        await Assert.That(parsed["Res.Empty"]).IsEqualTo("");
        await Assert.That(compiled["Res.Empty"]).IsEqualTo(parsed["Res.Empty"]);
    }

    [Test]
    public async Task An_ignorable_merged_dictionary_is_not_merged()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResIgnorableMerged.xaml");
        var compiled = Generated.FixturesResIgnorableMergedxamlXaml.Build();

        await Assert.That(parsed.MergedDictionaries.Count).IsEqualTo(0);
        await Assert.That(compiled.MergedDictionaries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_dynamic_resource_the_root_holds_outside_any_element_follows_the_key()
    {
        NoesisRuntime.Start();

        static string Followed(ResLiveRoot root)
        {
            var host = new StackPanel { Width = 100, Height = 100 };
            host.Resources = new ResourceDictionary();
            host.Resources["Res.LiveAccent"] = Colors.Red;
            var view = NoesisRuntime.Show(host, root);
            var first = ColorOf(root.Background);
            host.Resources["Res.LiveAccent"] = Colors.Blue;
            NoesisRuntime.Pump(view, host);
            return $"{first} -> {ColorOf(root.Background)}";
        }

        var parsed = (ResLiveRoot)GUI.LoadXaml("/Fixtures;Fixtures/ResLiveRoot.xaml");
        var compiled = new ResLiveRoot();
        compiled.InitializeComponent();

        await Assert.That(Followed(parsed)).IsEqualTo("#FFFF0000 -> #FF0000FF");
        await Assert.That(Followed(compiled)).IsEqualTo("#FFFF0000 -> #FF0000FF");
    }

    [Test]
    public async Task A_resource_element_holds_what_the_parser_finds_where_it_sits()
    {
        NoesisRuntime.Start();

        var parsed = Parsed("ResElements.xaml");
        var compiled = Generated.FixturesResElementsxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        foreach (var (dictionary, side) in new[] { (parsed, "parsed"), (compiled, "compiled") })
        {
            await Assert
                .That(((Border)dictionary["Res.MistypedChild"]).Child)
                .IsNull()
                .Because(side);
            await Assert
                .That(((StackPanel)dictionary["Res.MistypedItem"]).Children.Count)
                .IsEqualTo(1)
                .Because(side);
            await Assert
                .That(((ContentControl)dictionary["Res.AheadContent"]).Content)
                .IsNull()
                .Because(side);
        }

        static string Items(ResourceDictionary dictionary) =>
            string.Join(
                ",",
                ((ItemsControl)dictionary["Res.OutsideItems"])
                    .Items.Cast<object>()
                    .Select(item => ColorOf(item) ?? item.ToString())
            );

        await Assert.That(Items(parsed)).IsEqualTo("#FF3366CC,between,#80FFFFFF");
        await Assert.That(Items(compiled)).IsEqualTo(Items(parsed));
    }

    [Test]
    public async Task A_dynamic_resource_element_follows_the_key()
    {
        NoesisRuntime.Start();

        static string Followed(ResourceDictionary dictionary)
        {
            var content = (ContentControl)dictionary["Res.LiveContent"];
            dictionary.Remove("Res.LiveContent");
            var host = new StackPanel
            {
                Width = 100,
                Height = 100,
                Resources = new ResourceDictionary(),
            };
            host.Resources["Res.LiveText"] = "first";
            var view = NoesisRuntime.Show(host, content);
            var first = content.Content;
            host.Resources["Res.LiveText"] = "second";
            NoesisRuntime.Pump(view, host);
            return $"{first} -> {content.Content}";
        }

        var compiled = Generated.FixturesResElementsxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        await Assert.That(Followed(Parsed("ResElements.xaml"))).IsEqualTo("first -> second");
        await Assert.That(Followed(compiled)).IsEqualTo("first -> second");
    }

    [Test]
    public async Task A_style_left_to_the_parser_keeps_the_theme_style_it_is_based_on()
    {
        NoesisRuntime.Start();

        static Style Escaped(ResourceDictionary dictionary) =>
            new[] { dictionary }
                .Concat(dictionary.MergedDictionaries)
                .SelectMany(d => d.Values.Cast<object>())
                .OfType<Style>()
                .Single(style => style.TargetType == typeof(RadioButton));

        var parsed = Escaped(Parsed("ResEscapedBasedOn.xaml"));
        var compiled = Generated.FixturesResEscapedBasedOnxamlXaml.Build();
        Generated.XamlResources.Flush(compiled);

        await Assert.That(parsed.BasedOn?.TargetType).IsEqualTo(typeof(RadioButton));
        await Assert.That(Escaped(compiled).BasedOn?.TargetType).IsEqualTo(typeof(RadioButton));
    }
}
