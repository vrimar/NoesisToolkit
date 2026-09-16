using Noesis;
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

        var entries = XamlGenerated.NoesisToolkitEquivalenceTests.XamlRegistry.Dictionaries;
        await Assert.That(entries.Length).IsGreaterThan(0);

        var failures = new List<string>();
        foreach (var (path, logical, build) in entries)
        {
            var file = Path.GetFileName(path);

            // An opted-in document diverges in shape by construction; it is judged on value.
            if (
                File.ReadAllText(Path.Combine(XamlGraphDump.ProviderRoot(), path))
                    .Contains("ntk:CompileBindings", StringComparison.Ordinal)
            )
                continue;

            try
            {
                // The uri a document is reached by becomes its BaseUri, so it has to be rooted.
                var parsed = (ResourceDictionary)GUI.LoadXaml($"/{logical}");
                var generated = build();
                XamlGenerated.NoesisToolkitEquivalenceTests.XamlResources.Flush(generated);

                var expected = XamlGraphDump.Dump(parsed);
                var actual = XamlGraphDump.Dump(generated);
                Write(file, expected, actual);

                // Two empty dumps compare equal, which would turn every fixture green.
                if (Keys(expected) == 0)
                    failures.Add($"{file}: the parser produced no keys at all");
                else if (expected != actual)
                    failures.Add($"{file}: {Diff(expected, actual)}");
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    static int Keys(string dump) =>
        dump.Split('\n').Count(l => l.StartsWith("KEY ", StringComparison.Ordinal));

    [Test]
    public async Task The_compiled_root_matches_the_parsed_one()
    {
        Live();

        var compiled = new RootFixture();
        compiled.InitializeComponent();

        // Its constructor builds nothing, so the parser gets to fill this instance itself.
        var parsed = (RootFixture)GUI.LoadXaml("/Fixtures;Fixtures/Root.xaml");

        var expected = XamlGraphDump.Dump(parsed);
        var actual = XamlGraphDump.Dump(compiled);
        Write("Root.xaml", expected, actual);

        // A root's x:Name becomes a code-behind field; a template's stays in the template scope.
        await Assert.That(compiled.Frame).IsNotNull();
        await Assert.That(compiled.Stack).IsNotNull();
        await Assert.That(compiled.Action).IsNotNull();

        // Two bare headers compare equal, which would turn this green on a tree that never realized.
        await Assert
            .That(expected.Split('\n').Length)
            .IsGreaterThan(1)
            .Because("Root.xaml: the parser produced nothing below the root");

        await Assert
            .That(actual)
            .IsEqualTo(expected)
            .Because($"Root.xaml: {Diff(expected, actual)}");
    }

    [Test]
    public async Task Every_fixture_on_disk_is_compiled()
    {
        var dictionaries = Directory
            .EnumerateFiles(XamlGraphDump.FixtureRoot(), "*.xaml", SearchOption.AllDirectories)
            .Count(f => !File.ReadAllText(f).Contains("x:Class", StringComparison.Ordinal));

        // A document that stops matching AdditionalFiles is dropped with the build still green.
        await Assert
            .That(XamlGenerated.NoesisToolkitEquivalenceTests.XamlRegistry.Dictionaries.Length)
            .IsEqualTo(dictionaries);
    }

    static string Dumps
    {
        get
        {
            var directory = Path.Combine(Path.GetTempPath(), "noesistoolkit-equivalence");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    static void Write(string name, string expected, string actual)
    {
        File.WriteAllText(Path.Combine(Dumps, $"{name}.parsed.txt"), expected);
        File.WriteAllText(Path.Combine(Dumps, $"{name}.generated.txt"), actual);
    }

    static string Diff(string expected, string actual)
    {
        var left = expected.Split('\n');
        var right = actual.Split('\n');
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            var a = i < left.Length ? left[i] : "<eof>";
            var b = i < right.Length ? right[i] : "<eof>";
            if (a != b)
                return $"line {i + 1}: parsed '{a.Trim()}' vs compiled '{b.Trim()}' ({Dumps})";
        }

        return Dumps;
    }
}
