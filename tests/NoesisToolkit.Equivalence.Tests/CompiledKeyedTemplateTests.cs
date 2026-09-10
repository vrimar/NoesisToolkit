using Noesis;
using VertAlign = Noesis.VerticalAlignment;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class CompiledKeyedTemplateTests
{
    [Test]
    public async Task A_TemplateBinding_in_a_keyed_template_reads_the_host_it_is_applied_to()
    {
        NoesisRuntime.Start();

        await Assert.That(Shown(Compiled())).IsEqualTo("host-default");
        await Assert.That(Shown(Compiled())).IsEqualTo(Shown(Parsed()));
    }

    [Test]
    public async Task A_host_that_states_a_value_wins_over_the_default()
    {
        NoesisRuntime.Start();

        await Assert.That(Shown(Compiled(), "stated")).IsEqualTo("stated");
        await Assert.That(Shown(Compiled(), "stated")).IsEqualTo(Shown(Parsed(), "stated"));
    }

    // SetValue drops an enum silently, so a slot written the ordinary way keeps its own default
    // while the value the path produced goes nowhere.
    [Test]
    public async Task An_enum_slot_takes_the_value_a_native_binding_puts_there()
    {
        NoesisRuntime.Start();

        await Assert.That(Alignment(Compiled())).IsEqualTo(Alignment(Parsed()));
        await Assert.That(Alignment(Compiled())).IsEqualTo(VertAlign.Center);

        await Assert
            .That(Alignment(Compiled(), VertAlign.Bottom))
            .IsEqualTo(Alignment(Parsed(), VertAlign.Bottom));
        await Assert.That(Alignment(Compiled(), VertAlign.Bottom)).IsEqualTo(VertAlign.Bottom);
    }

    // One element carries one wiring slot, so a second binding on it used to replace the first.
    [Test]
    public async Task Every_binding_on_one_templated_element_is_wired()
    {
        NoesisRuntime.Start();

        var compiled = Realize(Compiled(), "both", VertAlign.Bottom).Aligned;
        var native = Realize(Parsed(), "both", VertAlign.Bottom).Aligned;
        await Assert.That(compiled).IsNotNull();
        await Assert.That(native).IsNotNull();

        await Assert.That(compiled!.Text).IsEqualTo("both");
        await Assert.That(((VertAlign?)compiled!.VerticalAlignment)).IsEqualTo(VertAlign.Bottom);

        await Assert.That(compiled!.Text).IsEqualTo(native!.Text);
        await Assert
            .That(((VertAlign?)compiled.VerticalAlignment))
            .IsEqualTo(native!.VerticalAlignment);
    }

    // The metadata default an enum property carries is stored as an integer, which is not a value
    // the property accepts back.
    [Test]
    public async Task An_unresolved_enum_path_leaves_the_registered_default()
    {
        NoesisRuntime.Start();

        var compiled = Unresolved(Compiled());
        await Assert.That(compiled).IsEqualTo(Unresolved(Parsed()));
        await Assert.That(compiled).IsEqualTo(VertAlign.Center);
    }

    static VertAlign Unresolved(ControlTemplate template) =>
        TreeSearch.Named<SpikeControl>(Host(template, null, null), "Unresolved")?.Align
        ?? VertAlign.Stretch;

    static ControlTemplate Compiled() =>
        (ControlTemplate)
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesTemplatedChromexamlXaml.Build()[
                "Chrome"
            ];

    static ControlTemplate Parsed() =>
        (ControlTemplate)
            ((ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/TemplatedChrome.xaml"))["Chrome"];

    static string? Shown(ControlTemplate template, string? label = null) =>
        Realize(template, label, null).Shown?.Text;

    static VertAlign Alignment(ControlTemplate template, VertAlign? align = null) =>
        Realize(template, null, align).Aligned?.VerticalAlignment ?? VertAlign.Stretch;

    static (TextBlock? Shown, TextBlock? Aligned) Realize(
        ControlTemplate template,
        string? label,
        VertAlign? align
    )
    {
        var host = Host(template, label, align);
        return (
            TreeSearch.Named<TextBlock>(host, "Shown"),
            TreeSearch.Named<TextBlock>(host, "Aligned")
        );
    }

    static SpikeControl Host(ControlTemplate template, string? label, VertAlign? align)
    {
        var host = new SpikeControl { Width = 800, Height = 600 };
        if (label is not null)
            host.Label = label;
        if (align is { } stated)
            host.Align = stated;

        host.Template = template;

        NoesisRuntime.Show(new Grid { Width = 800, Height = 600 }, host);

        return host;
    }
}
