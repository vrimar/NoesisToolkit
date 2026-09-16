using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public class TpHost : Control
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        "Ratio",
        typeof(double),
        typeof(TpHost),
        new FrameworkPropertyMetadata(0.0)
    );

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        "Mode",
        typeof(int),
        typeof(TpHost),
        new FrameworkPropertyMetadata(0)
    );

    public int Mode
    {
        get => (int)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        "Level",
        typeof(int),
        typeof(TpHost),
        new FrameworkPropertyMetadata(0)
    );

    public int Level
    {
        get => (int)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }
}

public class TpPart : Control
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        "Ratio",
        typeof(double),
        typeof(TpPart),
        new FrameworkPropertyMetadata(0.0)
    );

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }
}

public sealed class TpItem : INotifyPropertyChanged
{
    bool _flag;

    public bool Flag
    {
        get => _flag;
        set
        {
            _flag = value;
            Raise();
        }
    }

    int _index;

    public int Index
    {
        get => _index;
        set
        {
            _index = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TpFirst : IMultiValueConverter
{
    public object? Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture
    ) => values[0];

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture
    ) => throw new NotSupportedException();
}

[NotInParallel("Noesis")]
public sealed class CompiledTriggerPrecedenceTests
{
    static ResourceDictionary Compiled() =>
        XamlGenerated.NoesisToolkitEquivalenceTests.FixturesTpPrecedencexamlXaml.Build();

    static ResourceDictionary Parsed() =>
        (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/TpPrecedence.xaml");

    static TpHost Host(ResourceDictionary dictionary, string key, out View view)
    {
        var host = new TpHost
        {
            Width = 100,
            Height = 100,
            Ratio = 0.25,
        };
        if (dictionary[key] is Style style)
            host.Style = style;
        else
            host.Template = (ControlTemplate)dictionary[key];
        view = NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
        return host;
    }

    static double Ratio(TpHost host, string name) => TreeSearch.Named<TpPart>(host, name)!.Ratio;

    static bool Wired(FrameworkElement element) =>
        (int)element.GetValue(CompiledBindingSetup.IndexProperty) >= 0;

    [Test]
    [Arguments("NativeTrigger")]
    [Arguments("QualifiedTrigger")]
    [Arguments("CompiledTrigger")]
    [Arguments("MultiBound")]
    public async Task A_binding_a_template_trigger_targets_stays_below_that_trigger(string key)
    {
        NoesisRuntime.Start();

        var compiled = Host(Compiled(), key, out var compiledView);
        var native = Host(Parsed(), key, out var nativeView);

        async Task Step(Action<TpHost> act, double expected)
        {
            act(compiled);
            act(native);
            NoesisRuntime.Pump(compiledView, compiled);
            NoesisRuntime.Pump(nativeView, native);
            await Assert.That(Ratio(native, "Part")).IsEqualTo(expected);
            await Assert.That(Ratio(compiled, "Part")).IsEqualTo(Ratio(native, "Part"));
            await Assert.That(Ratio(compiled, "Free")).IsEqualTo(Ratio(native, "Free"));
        }

        await Step(_ => { }, 0.25);
        await Step(h => h.Mode = 1, 0.75);
        await Step(h => h.Ratio = 0.5, 0.75);
        await Step(h => h.Mode = 0, 0.5);
        await Step(h => h.Ratio = 0.125, 0.125);

        await Assert
            .That(Wired(TreeSearch.Named<TpPart>(compiled, "Free")!))
            .IsEqualTo(key is "CompiledTrigger" or "MultiBound");
        await Assert.That(Wired(TreeSearch.Named<TpPart>(compiled, "Part")!)).IsFalse();
    }

    [Test]
    [Arguments("StyleUnderNativeTrigger")]
    [Arguments("StyleUnderCompiledTrigger")]
    public async Task A_style_trigger_on_an_element_a_template_trigger_targets_stays_native(
        string key
    )
    {
        NoesisRuntime.Start();

        var compiled = Host(Compiled(), key, out var compiledView);
        var native = Host(Parsed(), key, out var nativeView);

        async Task Step(Action<TpHost> act, float expected)
        {
            act(compiled);
            act(native);
            NoesisRuntime.Pump(compiledView, compiled);
            NoesisRuntime.Pump(nativeView, native);
            var nativeOpacity = TreeSearch.Named<Border>(native, "Part")!.Opacity;
            await Assert.That(nativeOpacity).IsEqualTo(expected);
            await Assert
                .That(TreeSearch.Named<Border>(compiled, "Part")!.Opacity)
                .IsEqualTo(nativeOpacity);
        }

        await Step(h => h.Mode = 2, 0.5f);
        await Step(h => h.Level = 1, 0.25f);
        await Step(h => h.Mode = 0, 0.25f);
        await Step(h => h.Level = 0, 1f);

        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.Named<Border>(compiled, "Free")!))
            .IsNotEmpty();
        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.Named<Border>(compiled, "Part")!))
            .IsEmpty();
    }

    static (ContentControl Host, View View) Local(ResourceDictionary dictionary)
    {
        var host = new ContentControl
        {
            ContentTemplate = (DataTemplate)dictionary["LocalValues"],
            Content = new TpItem(),
            Width = 200,
            Height = 300,
        };
        return (host, NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host));
    }

    static void Flip(
        (ContentControl Host, View View) compiled,
        (ContentControl Host, View View) native,
        Action<TpItem> act
    )
    {
        act((TpItem)compiled.Host.Content);
        act((TpItem)native.Host.Content);
        NoesisRuntime.Pump(compiled.View, compiled.Host);
        NoesisRuntime.Pump(native.View, native.Host);
    }

    static Color Foreground(ContentControl host, string name) =>
        ((SolidColorBrush)TreeSearch.Named<TextBlock>(host, name)!.Foreground).Color;

    [Test]
    public async Task An_attached_spelling_of_the_property_outranks_a_style_trigger()
    {
        NoesisRuntime.Start();

        var compiled = Local(Compiled());
        var native = Local(Parsed());

        Flip(compiled, native, i => i.Flag = true);
        await Assert.That(Foreground(native.Host, "Aliased")).IsEqualTo(Colors.Red);
        await Assert
            .That(Foreground(compiled.Host, "Aliased"))
            .IsEqualTo(Foreground(native.Host, "Aliased"));
        await Assert.That(Foreground(native.Host, "Unaliased")).IsEqualTo(Colors.Green);
        await Assert
            .That(Foreground(compiled.Host, "Unaliased"))
            .IsEqualTo(Foreground(native.Host, "Unaliased"));

        await Assert
            .That(
                CompiledTriggerSet.SetsOf(TreeSearch.Named<TextBlock>(compiled.Host, "Unaliased")!)
            )
            .IsNotEmpty();
        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.Named<TextBlock>(compiled.Host, "Aliased")!))
            .IsEmpty();
    }

    static string Content(ContentControl host, string name) =>
        TreeSearch.Named<ContentControl>(host, name)!.Content switch
        {
            string text => text.Trim(),
            TextBlock block => "<" + block.Text + ">",
            var other => other?.ToString() ?? "<null>",
        };

    [Test]
    public async Task Implicit_content_outranks_a_style_trigger_on_the_content_property()
    {
        NoesisRuntime.Start();

        var compiled = Local(Compiled());
        var native = Local(Parsed());

        Flip(compiled, native, i => i.Flag = true);
        await Assert.That(Content(native.Host, "Texted")).IsEqualTo("hello");
        await Assert.That(Content(native.Host, "Elemented")).IsEqualTo("<child>");
        await Assert.That(Content(native.Host, "Empty")).IsEqualTo("triggered");
        foreach (var name in new[] { "Texted", "Elemented", "Empty" })
            await Assert.That(Content(compiled.Host, name)).IsEqualTo(Content(native.Host, name));

        await Assert
            .That(
                CompiledTriggerSet.SetsOf(TreeSearch.Named<ContentControl>(compiled.Host, "Empty")!)
            )
            .IsNotEmpty();
        await Assert
            .That(
                CompiledTriggerSet.SetsOf(
                    TreeSearch.Named<ContentControl>(compiled.Host, "Texted")!
                )
            )
            .IsEmpty();
    }

    [Test]
    public async Task A_native_setter_written_owner_qualified_contests_the_same_property()
    {
        NoesisRuntime.Start();

        var compiled = Local(Compiled());
        var native = Local(Parsed());

        async Task Expect(float expected)
        {
            var nativeOpacity = TreeSearch.Named<Border>(native.Host, "Qualified")!.Opacity;
            await Assert.That(nativeOpacity).IsEqualTo(expected);
            await Assert
                .That(TreeSearch.Named<Border>(compiled.Host, "Qualified")!.Opacity)
                .IsEqualTo(nativeOpacity);
        }

        Flip(compiled, native, i => i.Flag = true);
        await Expect(0.5f);
        Flip(compiled, native, i => i.Index = 1);
        await Expect(0.25f);
        Flip(compiled, native, i => i.Flag = false);
        await Expect(0.25f);
        Flip(compiled, native, i => i.Index = 0);
        await Expect(1f);

        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.Named<Border>(compiled.Host, "Lone")!))
            .IsNotEmpty();
        await Assert
            .That(CompiledTriggerSet.SetsOf(TreeSearch.Named<Border>(compiled.Host, "Qualified")!))
            .IsEmpty();
    }
}
