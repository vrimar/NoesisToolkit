using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests;

public partial class CpHost : ContentControl
{
    [DependencyProperty]
    public partial object? Extra { get; set; }
}

[NotInParallel("Noesis")]
public sealed class CompiledPresenterContentTests
{
    [Test]
    public async Task A_presenter_given_content_in_a_template_does_not_take_the_hosts_content()
    {
        NoesisRuntime.Start();

        var compiled = Realize(
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesCpPresenterxamlXaml.Build()
        );
        var parsed = Realize(
            (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/CpPresenter.xaml")
        );

        await Assert.That(parsed.Rendered).IsEqualTo("label|extra");
        await Assert.That(parsed.Warnings).IsEmpty();
        await Assert.That(compiled.Rendered).IsEqualTo(parsed.Rendered);
        await Assert.That(compiled.Warnings).IsEquivalentTo(parsed.Warnings);
    }

    static (string Rendered, List<string> Warnings) Realize(ResourceDictionary dictionary)
    {
        var warnings = new List<string>();
        Log.SetLogCallback(
            (level, _, message) =>
            {
                if (
                    level is LogLevel.Warning or LogLevel.Error
                    && message.Contains("ContentSource")
                )
                    warnings.Add(message);
            }
        );

        var control = new SpikeControl
        {
            Width = 100,
            Height = 50,
            Label = "label",
            Template = (ControlTemplate)dictionary["OnControl"],
        };
        var host = new CpHost
        {
            Width = 100,
            Height = 50,
            Extra = "extra",
            Content = "content",
            ContentTemplate = (DataTemplate)
                GUI.ParseXaml(
                    "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
                        + "<TextBlock Text=\"templated\"/></DataTemplate>"
                ),
            Template = (ControlTemplate)dictionary["OnContentControl"],
        };
        NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, control, host);

        static string Text(FrameworkElement root) =>
            string.Join(",", TreeSearch.All<TextBlock>(root).Select(t => t.Text));

        return ($"{Text(control)}|{Text(host)}", warnings);
    }
}
