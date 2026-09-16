using NoesisToolkit.Xaml;

namespace NoesisToolkit.Tests;

public class XamlDiagnosticsParityTests
{
    static GeneratorRun Run(string fixture, string host) =>
        GeneratorHarness.Run(
            new XamlCompileGenerator(),
            [fixture],
            [
                $$"""
                namespace Sample;

                public partial class {{host}} : global::Noesis.UserControl
                {
                    public {{host}}() => InitializeComponent();
                }
                """,
            ],
            new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" }
        );

    static List<string> Messages(GeneratorRun run, string id) =>
        run.GeneratorDiagnostics.Where(d => d.Id == id).Select(d => d.GetMessage()).ToList();

    [Test]
    public async Task A_duplicate_name_reports_the_conflict()
    {
        var run = Run("CbDupName.xaml", "CbDupHost");

        await Assert
            .That(Messages(run, "NTK1001"))
            .Contains("CbDupName.xaml :: x:Name='Same' is used more than once");
        await Assert
            .That(run.AllSources)
            .Contains("FAIL CbDupName.xaml :: x:Name='Same' is used more than once");
    }

    [Test]
    public async Task A_dynamic_resource_on_a_binding_knob_is_left_unset()
    {
        var run = Run("MkDynamicKnob.xaml", "MkKnobHost");

        var dead = Messages(run, "NTK1002");
        await Assert
            .That(dead)
            .Contains(
                "DEAD MkDynamicKnob.xaml :: Binding.ConverterParameter cannot take a {DynamicResource}"
            );
        await Assert
            .That(dead)
            .Contains(
                "DEAD MkDynamicKnob.xaml :: MultiBinding.FallbackValue cannot take a {DynamicResource}"
            );

        var source = run.Source("MkDynamicKnob");
        await Assert.That(source).DoesNotContain("DynamicResourceExtension");
        await Assert.That(source).DoesNotContain(".ConverterParameter =");
        await Assert.That(source).DoesNotContain(".FallbackValue =");
    }

    [Test]
    public async Task An_image_source_the_parser_rejects_is_refused()
    {
        var run = Run("MkImageRefusal.xaml", "MkImageHost");

        await Assert
            .That(Messages(run, "NTK1001"))
            .Contains(
                "MkImageRefusal.xaml :: image source 'a:b.png' is not a uri the parser is known to resolve"
            );
    }
}
