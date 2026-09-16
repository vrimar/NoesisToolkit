using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public partial class TcLoadedControl : UserControl
{
    public TcLoadedControl() => InitializeComponent();

    void Part_Click(object sender, MouseButtonEventArgs e) { }
}

public partial class TcBuiltControl : UserControl
{
    public TcBuiltControl() => InitializeComponent();
}

[NotInParallel("Noesis")]
public sealed class CompiledTemplateContentTests
{
    [Test]
    [Arguments("Loaded")]
    [Arguments("Built")]
    public async Task A_control_that_loads_its_own_content_is_cloned_as_the_parsed_template_clones_it(
        string key
    )
    {
        NoesisRuntime.Start();

        var compiled = Realize(
            () => XamlGenerated.NoesisToolkitEquivalenceTests.FixturesTcTemplatesxamlXaml.Build(),
            key
        );
        var parsed = Realize(
            () => (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/TcTemplates.xaml"),
            "Loaded"
        );

        await Assert.That(parsed.Warnings).IsEmpty();
        await Assert.That(compiled.Warnings).IsEquivalentTo(parsed.Warnings);
        await Assert.That(compiled.Shape).IsEqualTo(parsed.Shape);
    }

    static (List<string> Warnings, string Shape) Realize(Func<ResourceDictionary> load, string key)
    {
        var warnings = new List<string>();
        Log.SetLogCallback(
            (level, _, message) =>
            {
                if (level is LogLevel.Warning or LogLevel.Error)
                    warnings.Add(message);
            }
        );

        var template = (ControlTemplate)load()[key];
        var first = new SpikeControl
        {
            Width = 100,
            Height = 100,
            Template = template,
        };
        var second = new SpikeControl
        {
            Width = 100,
            Height = 100,
            Template = template,
        };
        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, first, second);

        var shape = string.Join(
            ";",
            new[] { first, second }
                .SelectMany(host => TreeSearch.All<UserControl>(host))
                .Select(control =>
                {
                    var part = control.FindName("Part") as FrameworkElement;
                    var named = TreeSearch
                        .All<FrameworkElement>(control)
                        .Where(e => e.Name.Length > 0);
                    return $"{control.Content?.GetType().Name}:{part is not null && TreeSearch.All<Border>(control).Contains(part)}:"
                        + string.Join(",", named.Select(e => e.Name));
                })
        );

        return (warnings.Where(w => w.Contains("duplicate name")).ToList(), shape);
    }
}
