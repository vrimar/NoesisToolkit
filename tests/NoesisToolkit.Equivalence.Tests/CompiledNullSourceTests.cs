using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public static partial class Marker
{
    public const string Unset = "unset";

    [DependencyProperty(Unset)]
    public static object Value { get; set; } = null!;
}

public sealed partial class MarkedHost : Control
{
    [DependencyProperty]
    public partial object? IsOpen { get; set; }
}

[NotInParallel("Noesis")]
public sealed class CompiledNullSourceTests
{
    [Test]
    [Arguments("FromTemplate")]
    [Arguments("FromParentPath")]
    public async Task A_null_source_property_is_written_as_null(string name)
    {
        NoesisRuntime.Start();

        var compiled = Marked(Compiled(), name);
        var parsed = Marked(Parsed(), name);

        await Assert.That(parsed).IsNull();
        await Assert.That(compiled).IsEqualTo(parsed);
    }

    [Test]
    public async Task A_null_context_read_whole_matches_the_native_binding()
    {
        NoesisRuntime.Start();

        var native = new Border();
        native.SetBinding(Marker.ValueProperty, new Binding());

        var compiled = new Border();
        CompiledBinding.Bind(compiled, Marker.ValueProperty, new CompiledBindingSpec());

        NoesisRuntime.Show(native, compiled);

        await Assert.That(Marker.GetValue(native)).IsNull();
        await Assert.That(Marker.GetValue(compiled)).IsEqualTo(Marker.GetValue(native));
    }

    [Test]
    public async Task A_null_context_read_through_a_path_matches_the_native_binding()
    {
        NoesisRuntime.Start();

        var native = new Border();
        native.SetBinding(Marker.ValueProperty, new Binding(nameof(SpikeItem.Label)));

        var compiled = new Border();
        CompiledBinding.Bind(
            compiled,
            Marker.ValueProperty,
            new CompiledBindingSpec { Hops = [Hop.Label] }
        );

        NoesisRuntime.Show(native, compiled);

        await Assert.That(Marker.GetValue(native)).IsEqualTo(Marker.Unset);
        await Assert.That(Marker.GetValue(compiled)).IsEqualTo(Marker.GetValue(native));
    }

    static object? Marked(ControlTemplate template, string name)
    {
        var host = new MarkedHost { Width = 400, Height = 300 };
        host.Template = template;
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);

        var border = TreeSearch.Named<Border>(host, name);
        return border is null ? "<missing>" : Marker.GetValue(border);
    }

    static ControlTemplate Compiled() =>
        (ControlTemplate)
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesNullSourcexamlXaml.Build()[
                "Marked"
            ];

    static ControlTemplate Parsed() =>
        (ControlTemplate)
            ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/NullSource.xaml"))["Marked"];
}
