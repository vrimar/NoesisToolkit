using NoesisToolkit.Xaml;

namespace NoesisToolkit.Tests;

public class NamespaceMapTests
{
    const string BehaviorsNs = "http://schemas.microsoft.com/xaml/behaviors";

    [Test]
    public async Task Presentation_uri_maps_to_the_Noesis_namespaces()
    {
        await Assert
            .That(Resolve("", XamlTypeResolver.PresentationNs))
            .IsEquivalentTo(["Noesis", "Noesis.Interactivity"]);
    }

    [Test]
    public async Task A_user_entry_extends_the_built_ins_rather_than_replacing_them()
    {
        await Assert
            .That(Resolve($"{BehaviorsNs}=MyApp.Behaviors", BehaviorsNs))
            .IsEquivalentTo(["Noesis.Interactivity", "MyApp.Behaviors"]);
    }

    [Test]
    public async Task An_unmapped_uri_yields_nothing()
    {
        await Assert.That(Resolve("", "http://example.com/unknown")).IsEmpty();
    }

    [Test]
    public async Task Several_entries_and_several_namespaces_parse()
    {
        await Assert
            .That(
                Resolve($"http://example.com/a=One,Two;{BehaviorsNs}=Three", "http://example.com/a")
            )
            .IsEquivalentTo(["One", "Two"]);
    }

    static string[] Resolve(string map, string uri) =>
        TestOptions.With(map).ClrNamespacesFor(uri).ToArray();
}
