using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class EdgeParameter : IValueConverter
{
    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => parameter?.ToString();

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value;
}

public sealed class EdgeRecorder : IValueConverter
{
    public readonly List<string> Seen = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Seen.Add(value?.GetType().Name ?? "null");
        return value?.ToString();
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value;
}

public sealed class EdgeTextBack : IValueConverter
{
    public readonly List<string> TargetTypes = new();

    public object? Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    ) => value?.ToString();

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        TargetTypes.Add(targetType.Name);
        return value?.ToString();
    }
}

public sealed class EdgeLeaf
{
    public string Name { get; set; } = "";
}

public sealed class EdgeItem : INotifyPropertyChanged
{
    int _count;
    EdgeItem? _selected;

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            Raise();
        }
    }

    public string Name { get; set; } = "";

    public decimal Price { get; set; } = 2.5m;

    public byte Small { get; set; } = 7;

    public char Letter { get; set; } = 'x';

    public EdgeItem? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Raise();
        }
    }

    public List<EdgeLeaf> Leaves { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

[NotInParallel("Noesis")]
public sealed class CompiledEdgeParityTests
{
    static ResourceDictionary Compiled() =>
        XamlGenerated.NoesisToolkitEquivalenceTests.FixturesEpEdgesxamlXaml.Build();

    static ResourceDictionary Parsed() =>
        (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/EpEdges.xaml");

    static (Control Host, View View) Show(
        ResourceDictionary dictionary,
        string key,
        object? context = null
    )
    {
        var host = new Control
        {
            Width = 400,
            Height = 300,
            Template = (ControlTemplate)dictionary[key],
            DataContext = context,
        };
        var view = NoesisRuntime.Show(new StackPanel { Width = 400, Height = 300 }, host);
        return (host, view);
    }

    [Test]
    public async Task An_escaped_comma_stays_in_the_argument()
    {
        NoesisRuntime.Start();

        string? Read(ResourceDictionary side) =>
            TreeSearch.Named<TextBlock>(Show(side, "Escaped").Host, "Out")?.Text;

        await Assert.That(Read(Parsed())).IsEqualTo("a,b");
        await Assert.That(Read(Compiled())).IsEqualTo(Read(Parsed()));
    }

    [Test]
    public async Task A_path_still_broken_writes_its_default_again_on_a_notification()
    {
        NoesisRuntime.Start();

        string Read(ResourceDictionary side)
        {
            var item = new EdgeItem();
            var (host, view) = Show(side, "Broken", item);
            var box = TreeSearch.Named<TextBox>(host, "Box")!;
            box.Text = "typed";
            NoesisRuntime.Pump(view, host);
            item.Raise(nameof(EdgeItem.Selected));
            NoesisRuntime.Pump(view, host);
            return box.Text;
        }

        await Assert.That(Read(Compiled())).IsEqualTo(Read(Parsed()));
    }

    [Test]
    public async Task A_converter_reads_a_narrow_number_as_the_type_the_engine_hands_it()
    {
        NoesisRuntime.Start();

        List<string> Seen(ResourceDictionary side)
        {
            Show(side, "Narrow", new EdgeItem());
            return ((EdgeRecorder)side["Recorder"]).Seen.Distinct().ToList();
        }

        await Assert.That(Seen(Compiled())).IsEquivalentTo(Seen(Parsed()));
    }

    [Test]
    public async Task A_value_handed_back_as_text_converts_into_the_source_as_the_engine_converts_it()
    {
        NoesisRuntime.Start();

        (int Count, List<string> TargetTypes) Write(ResourceDictionary side)
        {
            var item = new EdgeItem { Count = 7 };
            var (host, view) = Show(side, "Back", item);
            TreeSearch.Named<TextBox>(host, "Box")!.Text = "12";
            NoesisRuntime.Pump(view, host);
            return (item.Count, ((EdgeTextBack)side["TextBack"]).TargetTypes.Distinct().ToList());
        }

        var compiled = Write(Compiled());
        var parsed = Write(Parsed());

        await Assert.That(parsed.Count).IsEqualTo(12);
        await Assert.That(compiled.Count).IsEqualTo(parsed.Count);
        await Assert.That(compiled.TargetTypes).IsEquivalentTo(parsed.TargetTypes);
    }

    [Test]
    public async Task An_ancestor_walk_out_of_a_template_reads_the_container_it_lands_on()
    {
        NoesisRuntime.Start();

        string? Read(ResourceDictionary side)
        {
            var item = new EdgeItem { Name = "root" };
            item.Leaves.Add(new EdgeLeaf { Name = "leaf" });
            return TreeSearch.Named<TextBlock>(Show(side, "Tree", item).Host, "Up")?.Text;
        }

        await Assert.That(Read(Parsed())).IsEqualTo("leaf");
        await Assert.That(Read(Compiled())).IsEqualTo(Read(Parsed()));
    }
}
