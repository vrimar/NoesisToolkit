using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class RtWriteBackModel : INotifyPropertyChanged
{
    bool _refused;

    public bool Refused
    {
        get => _refused;
        set
        {
            _refused = false;
            Raise();
        }
    }

    public bool SilentlyRefused
    {
        get => false;
        set { }
    }

    int _count = 7;

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            Raise();
        }
    }

    string _upper = "";

    public string Upper
    {
        get => _upper;
        set
        {
            _upper = value.ToUpperInvariant();
            Raise();
        }
    }

    string _plain = "";

    public string Plain
    {
        get => _plain;
        set
        {
            _plain = value;
            Raise();
        }
    }

    string _trimmed = "";

    public string Trimmed
    {
        get => _trimmed;
        set
        {
            _trimmed = value.Trim();
            Raise();
        }
    }

    string _guarded = "kept";

    public string Guarded
    {
        get => _guarded;
        set
        {
            if (value == "boom")
                throw new InvalidOperationException("refused");

            _guarded = value;
            Raise();
        }
    }

    SpikeStage _stage = SpikeStage.Third;

    public SpikeStage Stage
    {
        get => _stage;
        set
        {
            _stage = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>The radio-button idiom: checked while the stage is the parameter, and unchecking hands
/// back a sentinel rather than a stage.</summary>
public abstract class RtStageIs : IValueConverter
{
    protected abstract object Declined { get; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => value is true ? Enum.Parse<SpikeStage>((string)parameter) : Declined;
}

public sealed class RtStageIsDoNothing : RtStageIs
{
    protected override object Declined => Binding.DoNothing;
}

public sealed class RtStageIsUnset : RtStageIs
{
    protected override object Declined => DependencyProperty.UnsetValue;
}

public sealed class RtBrackets : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        "[" + value + "]";

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => ((string)value).Trim('[', ']');
}

public sealed class RtEnterModel : INotifyPropertyChanged
{
    public int Writes;

    string _text = "kept";

    public string Text
    {
        get => _text;
        set
        {
            Writes++;
            _text = value;
            Raise();
        }
    }

    bool _flag = true;

    public bool Flag
    {
        get => _flag;
        set
        {
            Writes++;
            _flag = value;
            Raise();
        }
    }

    double _amount = 40;

    public double Amount
    {
        get => _amount;
        set
        {
            Writes++;
            _amount = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class RtEnterRoot : UserControl { }

[NotInParallel("Noesis")]
public sealed class CompiledWriteBackParityTests
{
    sealed class Side
    {
        internal Side(DataTemplate template)
        {
            Host = new ContentControl { Content = Model, ContentTemplate = template };
            Root = new Grid { Width = 400, Height = 600 };
            View = NoesisRuntime.Show(Root, Host);
            View.Activate();
        }

        internal RtWriteBackModel Model { get; } = new RtWriteBackModel();
        internal ContentControl Host { get; }
        internal Grid Root { get; }
        internal View View { get; }

        internal T Named<T>(string name)
            where T : FrameworkElement => TreeSearch.Named<T>(Root, name)!;

        internal bool Bound(string name, DependencyProperty property) =>
            BindingOperations.GetBindingExpressionBase(Named<FrameworkElement>(name), property)
                is not null;

        internal void Type(string text)
        {
            foreach (var ch in text)
                View.Char(ch);
        }

        internal void Pump() => NoesisRuntime.Pump(View, Root);
    }

    static (Side Native, Side Compiled) Realize()
    {
        NoesisRuntime.Start();
        var parsed = (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/RtWriteBack.xaml");
        var compiled =
            XamlGenerated.NoesisToolkitEquivalenceTests.FixturesRtWriteBackxamlXaml.Build();
        return (new Side((DataTemplate)parsed["Row"]), new Side((DataTemplate)compiled["Row"]));
    }

    static async Task AssertCompiled(
        Side native,
        Side compiled,
        string name,
        DependencyProperty property
    )
    {
        await Assert.That(native.Bound(name, property)).IsTrue();
        await Assert
            .That(compiled.Bound(name, property))
            .IsFalse()
            .Because($"{name} fell back to a native binding");
    }

    [Test]
    [Arguments("Refused")]
    [Arguments("SilentlyRefused")]
    public async Task A_setter_that_refuses_the_check_is_read_back_into_the_box(string name)
    {
        var (native, compiled) = Realize();
        await AssertCompiled(native, compiled, name, ToggleButton.IsCheckedProperty);

        native.Named<CheckBox>(name).IsChecked = true;
        compiled.Named<CheckBox>(name).IsChecked = true;

        await Assert.That(native.Named<CheckBox>(name).IsChecked).IsEqualTo(false);
        await Assert.That(compiled.Named<CheckBox>(name).IsChecked).IsEqualTo(false);
    }

    [Test]
    public async Task A_slider_over_an_int_snaps_to_what_the_source_kept()
    {
        var (native, compiled) = Realize();
        await AssertCompiled(native, compiled, "Snapped", RangeBase.ValueProperty);

        native.Named<Slider>("Snapped").Value = 2.7f;
        compiled.Named<Slider>("Snapped").Value = 2.7f;

        await Assert.That(native.Model.Count).IsEqualTo(2);
        await Assert.That(compiled.Model.Count).IsEqualTo(native.Model.Count);
        await Assert.That(native.Named<Slider>("Snapped").Value).IsEqualTo(2f);
        await Assert.That(compiled.Named<Slider>("Snapped").Value).IsEqualTo(2f);
    }

    [Test]
    [Arguments("Typed", "ab c ", "AB C ")]
    [Arguments("Bracketed", "abcd", "[bcda]")]
    public async Task Typing_shows_what_the_source_kept_with_the_caret_where_native_leaves_it(
        string name,
        string typed,
        string shown
    )
    {
        var (native, compiled) = Realize();
        await AssertCompiled(native, compiled, name, TextBox.TextProperty);

        string Trace(Side side)
        {
            var box = side.Named<TextBox>(name);
            box.Focus();
            side.Pump();

            var steps = new List<string>();
            foreach (var ch in typed)
            {
                side.Type(ch.ToString());
                steps.Add($"'{box.Text}'@{box.CaretIndex}");
            }

            return string.Join(" ", steps);
        }

        var nativeTrace = Trace(native);
        var compiledTrace = Trace(compiled);

        await Assert.That(native.Named<TextBox>(name).Text).IsEqualTo(shown);
        await Assert.That(compiledTrace).IsEqualTo(nativeTrace);
    }

    [Test]
    public async Task A_lost_focus_write_reads_the_trimmed_source_back_on_blur()
    {
        var (native, compiled) = Realize();
        await AssertCompiled(native, compiled, "Blurred", TextBox.TextProperty);

        foreach (var side in new[] { native, compiled })
        {
            side.Named<TextBox>("Blurred").Focus();
            side.Pump();
            side.Type(" ab ");
            side.Named<TextBox>("Elsewhere").Focus();
        }

        await Assert.That(native.Model.Trimmed).IsEqualTo("ab");
        await Assert.That(native.Named<TextBox>("Blurred").Text).IsEqualTo("ab");
        await Assert.That(compiled.Model.Trimmed).IsEqualTo("ab");
        await Assert.That(compiled.Named<TextBox>("Blurred").Text).IsEqualTo("ab");
    }

    [Test]
    public async Task A_setter_that_throws_is_read_back_into_the_box()
    {
        var (native, compiled) = Realize();
        await AssertCompiled(native, compiled, "Guarded", TextBox.TextProperty);

        native.Named<TextBox>("Guarded").Text = "boom";
        compiled.Named<TextBox>("Guarded").Text = "boom";

        await Assert.That(native.Named<TextBox>("Guarded").Text).IsEqualTo("kept");
        await Assert.That(compiled.Named<TextBox>("Guarded").Text).IsEqualTo("kept");
        await Assert.That(compiled.Model.Guarded).IsEqualTo("kept");
    }

    [Test]
    [Arguments("DoNothing")]
    [Arguments("Unset")]
    public async Task Unchecking_a_radio_whose_convert_back_declines_leaves_the_source_alone(
        string sentinel
    )
    {
        var (native, compiled) = Realize();
        await AssertCompiled(native, compiled, "Third" + sentinel, ToggleButton.IsCheckedProperty);

        await Assert.That(compiled.Named<CheckBox>("Third" + sentinel).IsChecked).IsEqualTo(true);

        native.Named<CheckBox>("Third" + sentinel).IsChecked = false;
        compiled.Named<CheckBox>("Third" + sentinel).IsChecked = false;
        native.Pump();
        compiled.Pump();

        await Assert.That(native.Model.Stage).IsEqualTo(SpikeStage.Third);
        await Assert.That(compiled.Model.Stage).IsEqualTo(SpikeStage.Third);

        foreach (var name in new[] { "First" + sentinel, "Third" + sentinel })
        {
            await Assert.That(native.Named<CheckBox>(name).IsChecked).IsEqualTo(false);
            await Assert
                .That(compiled.Named<CheckBox>(name).IsChecked)
                .IsEqualTo(native.Named<CheckBox>(name).IsChecked);
        }
    }

    static TextBox Box(RtEnterRoot root, string name) => (TextBox)root.FindName(name);

    static (RtEnterRoot Root, RtEnterModel Model) Entered(bool compiled)
    {
        NoesisRuntime.Start();
        var model = new RtEnterModel();

        RtEnterRoot root;
        if (compiled)
        {
            root = new RtEnterRoot { DataContext = model };
            root.InitializeComponent();
        }
        else
        {
            root = (RtEnterRoot)GUI.LoadXaml("/Fixtures;Fixtures/RtEnterRoot.xaml");
            root.DataContext = model;
        }

        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, root).Activate();
        return (root, model);
    }

    [Test]
    public async Task Entering_a_view_writes_nothing_back_to_the_source()
    {
        var (native, nativeModel) = Entered(compiled: false);
        var (compiled, compiledModel) = Entered(compiled: true);

        await Assert
            .That(
                BindingOperations.GetBindingExpressionBase(
                    Box(compiled, "Typed"),
                    TextBox.TextProperty
                )
            )
            .IsNull();
        await Assert.That(Box(native, "Typed").Text).IsEqualTo("kept");
        await Assert.That(Box(compiled, "Typed").Text).IsEqualTo("kept");
        await Assert.That(nativeModel.Writes).IsEqualTo(0);
        await Assert.That(compiledModel.Writes).IsEqualTo(nativeModel.Writes);
    }

    [Test]
    public async Task Leaving_a_lost_focus_box_without_an_edit_still_writes_the_source()
    {
        var sides = new[] { Entered(compiled: false), Entered(compiled: true) };

        foreach (var (root, _) in sides)
        {
            Box(root, "Blurred").Focus();
            Box(root, "Elsewhere").Focus();
        }

        await Assert.That(sides[0].Model.Writes).IsEqualTo(1);
        await Assert.That(sides[1].Model.Writes).IsEqualTo(sides[0].Model.Writes);
    }
}
