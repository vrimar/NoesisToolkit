using Noesis;
using NoesisToolkit.Testing;
using Path = System.IO.Path;

namespace NoesisToolkit.Equivalence.Tests;

// Proves shape, not timing: both sides are compared with the graph already flushed, so a lookup
// performed too early still resolves here.
[NotInParallel("Noesis")]
public sealed class EquivalenceTests
{
    static void Live() => NoesisRuntime.Start();

    [Test]
    public async Task Every_compiled_dictionary_matches_the_parsed_graph()
    {
        Live();

        var failures = XamlParity.Check(
            XamlGenerated.NoesisToolkitEquivalenceTests.XamlRegistry.Dictionaries,
            XamlGenerated.NoesisToolkitEquivalenceTests.XamlResources.Flush,
            Dumps
        );

        await Assert.That(string.Join("\n", failures)).IsEmpty();
    }

    [Test]
    public async Task The_compiled_root_matches_the_parsed_one()
    {
        Live();

        var compiled = new RootFixture();
        compiled.InitializeComponent();

        // Its constructor builds nothing, so the parser gets to fill this instance itself.
        var parsed = (RootFixture)GUI.LoadXaml("/Fixtures;Fixtures/Root.xaml");

        // A root's x:Name becomes a code-behind field; a template's stays in the template scope.
        await Assert.That(compiled.Frame).IsNotNull();
        await Assert.That(compiled.Stack).IsNotNull();
        await Assert.That(compiled.Action).IsNotNull();

        await Assert
            .That(XamlParity.Compare("Root.xaml", parsed, compiled, Dumps)?.ToString())
            .IsNull();
    }

    [Test]
    public async Task Every_fixture_on_disk_is_compiled()
    {
        var dictionaries = Directory
            .EnumerateFiles(FixtureFiles.FixtureRoot(), "*.xaml", SearchOption.AllDirectories)
            .Count(f => !File.ReadAllText(f).Contains("x:Class", StringComparison.Ordinal));

        // A document that stops matching AdditionalFiles is dropped with the build still green.
        await Assert
            .That(XamlGenerated.NoesisToolkitEquivalenceTests.XamlRegistry.Dictionaries.Length)
            .IsEqualTo(dictionaries);
    }

    [Test]
    public async Task An_application_value_compares_by_its_type_not_its_ToString()
    {
        Live();

        static Border Stamped() => new() { Tag = new Stamp(), Child = new TextBlock() };

        await Assert.That(XamlParity.Compare("Stamped", Stamped(), Stamped())?.ToString()).IsNull();
    }

    sealed class Stamp
    {
        readonly Guid _id = Guid.NewGuid();

        public override string ToString() => $"Stamp[{_id}]";
    }

    static string Dumps => Path.Combine(Path.GetTempPath(), "noesistoolkit-equivalence");
}
