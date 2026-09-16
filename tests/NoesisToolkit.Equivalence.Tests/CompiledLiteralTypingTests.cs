using System.ComponentModel;
using System.Runtime.CompilerServices;
using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class LtItem : INotifyPropertyChanged
{
    public long Big { get; set; } = 5;

    public byte Small { get; set; } = 5;

    public sbyte Tiny { get; set; } = -5;

    public short Mid { get; set; } = -5;

    public ushort UMid { get; set; } = 5;

    public uint UBig { get; set; } = 5;

    public ulong Huge { get; set; } = 5;

    public long? MaybeBig { get; set; } = 5;

    public object? Boxed { get; set; } = 5;

    public object? Nothing { get; set; }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class LtControl : Control
{
    public static readonly DependencyProperty BigProperty = DependencyProperty.Register(
        "Big",
        typeof(long),
        typeof(LtControl),
        new FrameworkPropertyMetadata(0L)
    );

    public long Big
    {
        get => (long)GetValue(BigProperty);
        set => SetValue(BigProperty, value);
    }

    public static readonly DependencyProperty SmallProperty = DependencyProperty.Register(
        "Small",
        typeof(byte),
        typeof(LtControl),
        new FrameworkPropertyMetadata((byte)0)
    );

    public byte Small
    {
        get => (byte)GetValue(SmallProperty);
        set => SetValue(SmallProperty, value);
    }
}

[NotInParallel("Noesis")]
public sealed class CompiledLiteralTypingTests
{
    static ResourceDictionary Compiled() =>
        XamlGenerated.NoesisToolkitEquivalenceTests.FixturesLtLiteralsxamlXaml.Build();

    static ResourceDictionary Parsed() =>
        (ResourceDictionary)GUI.LoadXaml("/Fixtures;Fixtures/LtLiterals.xaml");

    static (ContentControl Host, View View) Realize(ResourceDictionary dictionary, string key)
    {
        var host = new ContentControl
        {
            ContentTemplate = (DataTemplate)dictionary[key],
            Content = new LtItem(),
            Width = 200,
            Height = 200,
        };
        return (host, NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host));
    }

    static LtControl Templated(ResourceDictionary dictionary, string key)
    {
        var host = new LtControl
        {
            Width = 100,
            Height = 100,
            Big = 5,
            Template = (ControlTemplate)dictionary[key],
        };
        NoesisRuntime.Show(new Grid { Width = 400, Height = 300 }, host);
        return host;
    }

    [Test]
    [Arguments("Big")]
    [Arguments("Small")]
    [Arguments("Tiny")]
    [Arguments("Mid")]
    [Arguments("UMid")]
    [Arguments("UBig")]
    [Arguments("Huge")]
    [Arguments("MaybeBig")]
    public async Task A_data_condition_on_an_integer_path_holds_whatever_its_width(string name)
    {
        NoesisRuntime.Start();

        var compiled = TreeSearch.Named<Border>(Realize(Compiled(), "Conditions").Host, name)!;
        var native = TreeSearch.Named<Border>(Realize(Parsed(), "Conditions").Host, name)!;

        await Assert.That(native.Opacity).IsEqualTo(0.5f);
        await Assert.That(compiled.Opacity).IsEqualTo(native.Opacity);
        await Assert.That(CompiledTriggerSet.SetsOf(compiled)).IsNotEmpty();
    }

    // Native converts the constant to the path's run-time type, which no build-time literal can.
    [Test]
    public async Task A_data_condition_on_an_object_path_stays_native()
    {
        NoesisRuntime.Start();

        var compiled = Realize(Compiled(), "Conditions").Host;
        var native = Realize(Parsed(), "Conditions").Host;

        var boxed = TreeSearch.Named<Border>(compiled, "Boxed")!;
        await Assert.That(TreeSearch.Named<Border>(native, "Boxed")!.Opacity).IsEqualTo(0.5f);
        await Assert
            .That(boxed.Opacity)
            .IsEqualTo(TreeSearch.Named<Border>(native, "Boxed")!.Opacity);
        await Assert.That(CompiledTriggerSet.SetsOf(boxed)).IsEmpty();

        var nothing = TreeSearch.Named<Border>(compiled, "Nothing")!;
        await Assert.That(CompiledTriggerSet.SetsOf(nothing)).IsNotEmpty();
        await Assert.That(TreeSearch.Named<Border>(native, "Nothing")!.Opacity).IsEqualTo(0.5f);
        await Assert
            .That(nothing.Opacity)
            .IsEqualTo(TreeSearch.Named<Border>(native, "Nothing")!.Opacity);
    }

    [Test]
    public async Task A_style_setter_on_a_non_int32_slot_is_applied()
    {
        NoesisRuntime.Start();

        var compiled = TreeSearch.Named<LtControl>(Realize(Compiled(), "Setters").Host, "Styled")!;
        var native = TreeSearch.Named<LtControl>(Realize(Parsed(), "Setters").Host, "Styled")!;

        await Assert.That(native.Big).IsEqualTo(9L);
        await Assert.That(native.Small).IsEqualTo((byte)9);
        await Assert.That(compiled.Big).IsEqualTo(native.Big);
        await Assert.That(compiled.Small).IsEqualTo(native.Small);
        await Assert.That(((Setter)compiled.Style.Setters[0]).Value).IsTypeOf<long>();
    }

    [Test]
    public async Task A_compiled_trigger_setter_on_a_non_int32_slot_applies_and_reverts()
    {
        NoesisRuntime.Start();

        var (compiledHost, compiledView) = Realize(Compiled(), "Setters");
        var (nativeHost, nativeView) = Realize(Parsed(), "Setters");
        var compiled = TreeSearch.Named<LtControl>(compiledHost, "Triggered")!;
        var native = TreeSearch.Named<LtControl>(nativeHost, "Triggered")!;
        await Assert.That(CompiledTriggerSet.SetsOf(compiled)).IsNotEmpty();

        ((LtItem)compiledHost.Content).Flag = true;
        ((LtItem)nativeHost.Content).Flag = true;
        NoesisRuntime.Pump(compiledView, compiledHost);
        NoesisRuntime.Pump(nativeView, nativeHost);
        await Assert.That(native.Big).IsEqualTo(7L);
        await Assert.That(native.Small).IsEqualTo((byte)7);
        await Assert.That(compiled.Big).IsEqualTo(native.Big);
        await Assert.That(compiled.Small).IsEqualTo(native.Small);

        ((LtItem)compiledHost.Content).Flag = false;
        ((LtItem)nativeHost.Content).Flag = false;
        NoesisRuntime.Pump(compiledView, compiledHost);
        NoesisRuntime.Pump(nativeView, nativeHost);
        await Assert.That(native.Big).IsEqualTo(0L);
        await Assert.That(compiled.Big).IsEqualTo(native.Big);
        await Assert.That(compiled.Small).IsEqualTo(native.Small);
    }

    [Test]
    public async Task A_compiled_property_condition_on_an_int64_slot_holds()
    {
        NoesisRuntime.Start();

        var compiled = Templated(Compiled(), "CompiledPropertyTrigger");
        var native = Templated(Parsed(), "CompiledPropertyTrigger");
        var compiledBg = TreeSearch.Named<Border>(compiled, "Bg")!;

        await Assert.That(TreeSearch.Named<Border>(native, "Bg")!.Opacity).IsEqualTo(0.5f);
        await Assert
            .That(compiledBg.Opacity)
            .IsEqualTo(TreeSearch.Named<Border>(native, "Bg")!.Opacity);
        await Assert.That(CompiledTriggerSet.SetsOf(compiledBg)).IsNotEmpty();
    }

    [Test]
    public async Task A_native_property_trigger_on_an_int64_slot_holds()
    {
        NoesisRuntime.Start();

        var dictionary = Compiled();
        var compiled = Templated(dictionary, "NativePropertyTrigger");
        var native = Templated(Parsed(), "NativePropertyTrigger");
        var compiledBg = TreeSearch.Named<Border>(compiled, "Bg")!;

        var trigger = (Trigger)((ControlTemplate)dictionary["NativePropertyTrigger"]).Triggers[0];
        await Assert.That(TreeSearch.Named<Border>(native, "Bg")!.Opacity).IsEqualTo(0.5f);
        await Assert
            .That(compiledBg.Opacity)
            .IsEqualTo(TreeSearch.Named<Border>(native, "Bg")!.Opacity);
        await Assert.That(CompiledTriggerSet.SetsOf(compiledBg)).IsEmpty();
        await Assert.That(trigger.Value).IsTypeOf<long>();
    }
}
