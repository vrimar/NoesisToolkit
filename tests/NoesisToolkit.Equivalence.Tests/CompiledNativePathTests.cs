using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledNativePathTests
{
    [Test]
    [Arguments("Direct")]
    [Arguments("Nested")]
    [Arguments("Multi")]
    public async Task A_prefixed_attached_property_in_a_native_binding_path_resolves(string name)
    {
        NoesisRuntime.Start();

        var compiled = Realize(Compiled(), name);
        var parsed = Realize(Parsed(), name);

        await Assert.That(parsed.Text).IsEqualTo("marked");
        await Assert.That(compiled.Text).IsEqualTo(parsed.Text);
        await Assert
            .That(BindingOperations.GetBindingExpressionBase(compiled, TextBlock.TextProperty))
            .IsNotNull();
    }

    static TextBlock Realize(ControlTemplate template, string name)
    {
        var inner = new Border();
        Marker.SetValue(inner, "marked");

        var host = new SpikeControl
        {
            Width = 400,
            Height = 300,
            Tag = inner,
        };
        Marker.SetValue(host, "marked");
        host.Template = template;
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);

        return TreeSearch.Named<TextBlock>(host, name)!;
    }

    static ControlTemplate Compiled() =>
        (ControlTemplate)
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesNpPathsxamlXaml.Build()["Paths"];

    static ControlTemplate Parsed() =>
        (ControlTemplate)
            ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/NpPaths.xaml"))["Paths"];
}
