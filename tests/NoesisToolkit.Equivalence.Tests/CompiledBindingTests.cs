using System.Globalization;
using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// Judged against what a native Binding puts in the same slot.
[NotInParallel("Noesis")]
public sealed class CompiledBindingTests
{
    static CompiledBindingSpec Spec() =>
        new() { Hops = [Hop.Label], Convert = v => (string?)v ?? "" };

    [Test]
    public async Task It_renders_what_a_native_binding_renders()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Label = "first" };
        var compiled = new SpikeItem { Label = "first" };

        var (nativeText, compiledText) = Realize(native, compiled);
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
        await Assert.That(compiledText.Text).IsEqualTo("first");

        native.Label = "second";
        compiled.Label = "second";
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
        await Assert.That(compiledText.Text).IsEqualTo("second");
    }

    [Test]
    public async Task It_follows_a_swapped_DataContext()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Label = "before" };
        var compiled = new SpikeItem { Label = "before" };
        var (nativeText, compiledText) = Realize(native, compiled);

        nativeText.DataContext = new SpikeItem { Label = "after" };
        compiledText.DataContext = new SpikeItem { Label = "after" };

        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
        await Assert.That(compiledText.Text).IsEqualTo("after");
    }

    [Test]
    public async Task It_survives_being_unloaded_and_loaded_again()
    {
        NoesisRuntime.Start();

        var source = new SpikeItem { Label = "before" };
        var target = new TextBlock { DataContext = source };
        CompiledBinding.Bind(target, TextBlock.TextProperty, Spec());

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, target);
        await Assert.That(target.Text).IsEqualTo("before");

        // A virtualizing panel recycles containers exactly this way.
        root.Children.Remove(target);
        NoesisRuntime.Pump(view, root);
        root.Children.Add(target);
        NoesisRuntime.Pump(view, root);

        source.Label = "after";
        await Assert.That(target.Text).IsEqualTo("after");
    }

    [Test]
    public async Task An_unresolved_path_leaves_the_slot_where_a_native_binding_leaves_it()
    {
        NoesisRuntime.Start();

        var spec = new CompiledBindingSpec
        {
            Hops = [Hop.Flag],
            Convert = v => v is bool flag && flag,
        };

        var native = new Button();
        native.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(SpikeItem.Flag)));

        var compiled = new Button();
        CompiledBinding.Bind(compiled, UIElement.IsEnabledProperty, spec);

        NoesisRuntime.Show(native, compiled);

        // Neither has a DataContext, so the path never runs and IsEnabled's true default survives.
        await Assert.That(compiled.IsEnabled).IsEqualTo(native.IsEnabled);
        await Assert.That(compiled.IsEnabled).IsTrue();

        native.DataContext = new SpikeItem { Flag = false };
        compiled.DataContext = new SpikeItem { Flag = false };
        await Assert.That(compiled.IsEnabled).IsEqualTo(native.IsEnabled);
        await Assert.That(compiled.IsEnabled).IsFalse();

        native.DataContext = null;
        compiled.DataContext = null;
        await Assert.That(compiled.IsEnabled).IsEqualTo(native.IsEnabled);
    }

    [Test]
    public async Task An_unresolved_path_does_not_reach_the_converter()
    {
        NoesisRuntime.Start();

        var converter = new StampConverter();
        var spec = new CompiledBindingSpec
        {
            Hops = [Hop.Label],
            Convert = v => (string?)v ?? "",
            Converter = converter,
            TargetType = typeof(string),
        };

        var native = new TextBlock();
        native.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(SpikeItem.Label)) { Converter = converter }
        );

        var compiled = new TextBlock();
        CompiledBinding.Bind(compiled, TextBlock.TextProperty, spec);

        NoesisRuntime.Show(native, compiled);

        await Assert.That(compiled.Text).IsEqualTo(native.Text);

        native.DataContext = new SpikeItem { Label = "here" };
        compiled.DataContext = new SpikeItem { Label = "here" };
        await Assert.That(compiled.Text).IsEqualTo(native.Text);
        await Assert.That(compiled.Text).IsEqualTo("stamped-here");

        // A template realized without data sets DataContext to null outright, which is not the
        // same slot state as never having set it.
        native.DataContext = null;
        compiled.DataContext = null;
        await Assert.That(compiled.Text).IsEqualTo(native.Text);
    }

    [Test]
    public async Task An_unresolved_path_does_not_hand_the_slot_back_to_the_style()
    {
        NoesisRuntime.Start();

        var spec = new CompiledBindingSpec
        {
            Hops = [Hop.Label],
            Convert = v => v is string name ? new SolidColorBrush(Colors.Blue) : null,
        };

        var styled = new Style { TargetType = typeof(TextBlock) };
        styled.Setters.Add(
            new Setter
            {
                Property = TextBlock.ForegroundProperty,
                Value = new SolidColorBrush(Colors.Red),
            }
        );

        var native = new TextBlock { Style = styled };
        native.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(SpikeItem.Label)));

        var compiled = new TextBlock { Style = styled };
        CompiledBinding.Bind(compiled, TextBlock.ForegroundProperty, spec);

        var item = new SpikeItem { Label = "set" };
        native.DataContext = item;
        compiled.DataContext = item;

        NoesisRuntime.Show(native, compiled);

        // The binding has to own the slot first, or "does not hand it back" proves nothing.
        await Assert
            .That(Describe(compiled.Foreground))
            .IsEqualTo(Describe(new SolidColorBrush(Colors.Blue)));

        native.DataContext = null;
        compiled.DataContext = null;

        await Assert.That(Describe(compiled.Foreground)).IsEqualTo(Describe(native.Foreground));
        await Assert
            .That(Describe(compiled.Foreground))
            .IsNotEqualTo(Describe(new SolidColorBrush(Colors.Red)));
    }

    static string Describe(Brush? brush) =>
        brush is SolidColorBrush solid ? solid.Color.ToString() : brush?.ToString() ?? "null";

    [Test]
    public async Task An_attached_property_takes_a_compiled_binding()
    {
        NoesisRuntime.Start();

        var spec = new CompiledBindingSpec
        {
            Hops = [Hop.Index],
            Convert = v => v is int index ? index : 0,
        };

        var source = new SpikeItem { Index = 7 };

        var native = new TextBlock { DataContext = source };
        native.SetBinding(SpikeHook.SetupProperty, new Binding(nameof(SpikeItem.Index)));

        var compiled = new TextBlock { DataContext = source };
        CompiledBinding.Bind(compiled, SpikeHook.SetupProperty, spec);

        NoesisRuntime.Show(native, compiled);

        await Assert.That(SpikeHook.GetSetup(compiled)).IsEqualTo(SpikeHook.GetSetup(native));
        await Assert.That(SpikeHook.GetSetup(compiled)).IsEqualTo(7);

        source.Index = 9;
        await Assert.That(SpikeHook.GetSetup(compiled)).IsEqualTo(SpikeHook.GetSetup(native));
        await Assert.That(SpikeHook.GetSetup(compiled)).IsEqualTo(9);
    }

    static (TextBlock Native, TextBlock Compiled) Realize(SpikeItem native, SpikeItem compiled)
    {
        var nativeText = new TextBlock { DataContext = native };
        nativeText.SetBinding(TextBlock.TextProperty, new Binding(nameof(SpikeItem.Label)));

        var compiledText = new TextBlock { DataContext = compiled };
        CompiledBinding.Bind(compiledText, TextBlock.TextProperty, Spec());

        NoesisRuntime.Show(nativeText, compiledText);
        return (nativeText, compiledText);
    }
}
