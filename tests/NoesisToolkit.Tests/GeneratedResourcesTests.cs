using System.Reflection;
using NoesisToolkit.Xaml;

namespace NoesisToolkit.Tests;

/// <summary>Runs the generated code, which is the only way the resource-timing rules are observable.</summary>
public class GeneratedResourcesTests
{
    static Assembly Build(params string[] fixtures) =>
        GeneratorHarness.Execute(
            new XamlCompileGenerator(),
            fixtures,
            new Dictionary<string, string> { ["build_property.ProjectDir"] = "/repo/App" }
        );

    static Type Resources(Assembly assembly) =>
        assembly.GetType("XamlGenerated.SampleApp.XamlResources")!;

    static object? Call(Type type, string method, params object?[] args) =>
        type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);

    [Test]
    public async Task Resolve_builds_a_fresh_dictionary_each_time()
    {
        var resources = Resources(Build("Theme.xaml"));

        var first = Call(resources, "Resolve", "SampleApp;Theme.xaml");
        var second = Call(resources, "Resolve", "SampleApp;Theme.xaml");

        await Assert.That(first).IsNotNull();
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }

    [Test]
    public async Task SharedByKey_returns_the_same_value_every_time()
    {
        var resources = Resources(Build("Theme.xaml"));

        var first = Call(resources, "SharedByKey", "BaseButton");
        var second = Call(resources, "SharedByKey", "BaseButton");

        await Assert.That(first).IsNotNull();
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task A_sibling_key_is_unresolved_until_Flush()
    {
        var assembly = Build("Theme.xaml");
        var resources = Resources(assembly);
        var registry = assembly.GetType("XamlGenerated.SampleApp.XamlRegistry")!;

        var entries = (Array)
            registry
                .GetField("Dictionaries", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
        await Assert.That(entries.Length).IsEqualTo(1);

        var dictionary = Call(resources, "Resolve", "SampleApp;Theme.xaml")!;
        var wide = Indexer(dictionary, "WideButton")!;
        var basedOn = wide.GetType().GetProperty("BasedOn")!;

        await Assert.That(basedOn.GetValue(wide)).IsNull();

        Call(resources, "Flush", dictionary);

        await Assert.That(basedOn.GetValue(wide)).IsEqualTo(Indexer(dictionary, "BaseButton"));
    }

    [Test]
    public async Task A_merged_dictionary_is_resolved_through_the_registry()
    {
        var assembly = Build("App.xaml", "Palette.xaml");
        var resources = Resources(assembly);

        var app = Call(resources, "Resolve", "SampleApp;App.xaml")!;
        var merged = (System.Collections.IList)
            app.GetType().GetProperty("MergedDictionaries")!.GetValue(app)!;

        await Assert.That(merged.Count).IsEqualTo(1);
        await Assert.That(Indexer(merged[0]!, "Accent")).IsNotNull();
    }

    [Test]
    public async Task A_key_owned_by_a_sibling_file_resolves_through_SharedByKey()
    {
        var resources = Resources(Build("App.xaml", "Palette.xaml"));

        await Assert.That(Call(resources, "SharedByKey", "Accent")).IsNotNull();
    }

    static object? Indexer(object dictionary, object key) =>
        dictionary.GetType().GetProperty("Item")!.GetValue(dictionary, [key]);
}
