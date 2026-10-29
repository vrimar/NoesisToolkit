using Noesis;
using Generated = XamlGenerated.NoesisToolkitEquivalenceTests;

namespace NoesisToolkit.Equivalence.Tests;

/// <summary>Runs the generated code, which is the only way the resource-timing rules are observable.</summary>
[NotInParallel("Noesis")]
public sealed class GeneratedResourcesTests
{
    const string Styles = "Fixtures;Fixtures/Styles.xaml";

    [Test]
    public async Task Resolve_builds_a_fresh_dictionary_each_time()
    {
        NoesisRuntime.Start();

        var first = Generated.XamlResources.Resolve(Styles);
        var second = Generated.XamlResources.Resolve(Styles);

        await Assert.That(first).IsNotNull();
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }

    [Test]
    public async Task SharedByKey_returns_the_same_value_every_time()
    {
        NoesisRuntime.Start();

        var first = Generated.XamlResources.SharedByKey("BaseButton");
        var second = Generated.XamlResources.SharedByKey("BaseButton");

        await Assert.That(first).IsNotNull();
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task A_key_declared_earlier_in_the_document_resolves_as_it_is_built()
    {
        NoesisRuntime.Start();

        var dictionary = Generated.XamlResources.Resolve(Styles);
        var wide = (Style)dictionary["WideButton"];

        await Assert.That(wide.BasedOn).IsEqualTo(dictionary["BaseButton"]);
    }

    [Test]
    public async Task A_merged_dictionary_is_resolved_through_the_registry()
    {
        NoesisRuntime.Start();

        var merged = Generated.XamlResources.Resolve(Styles).MergedDictionaries;

        await Assert.That(merged.Count).IsEqualTo(1);
        await Assert.That(merged[0].Contains("Pad")).IsTrue();
    }

    [Test]
    public async Task A_key_owned_by_a_sibling_file_resolves_through_SharedByKey()
    {
        NoesisRuntime.Start();

        await Assert.That(Generated.XamlResources.SharedByKey("Pad")).IsNotNull();
    }
}
