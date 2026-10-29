using System.Globalization;
using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

public sealed partial class LimitedHost : Control
{
    [DependencyProperty]
    public partial int? Limit { get; set; }
}

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

    [Test]
    public async Task An_unresolved_path_does_not_hand_a_nullable_slot_back_to_the_style()
    {
        NoesisRuntime.Start();

        var styled = new Style { TargetType = typeof(LimitedHost) };
        styled.Setters.Add(new Setter { Property = LimitedHost.LimitProperty, Value = 5 });

        var native = new LimitedHost { Style = styled, DataContext = new SpikeItem() };
        native.SetBinding(LimitedHost.LimitProperty, new Binding("Payload.Index"));

        var compiled = new LimitedHost { Style = styled, DataContext = new SpikeItem() };
        CompiledBinding.Bind(
            compiled,
            LimitedHost.LimitProperty,
            new CompiledBindingSpec
            {
                Hops =
                [
                    Hop.Payload,
                    BindingHop.Guarded<SpikeItem>(nameof(SpikeItem.Index), s => s.Index),
                ],
            }
        );

        NoesisRuntime.Show(native, compiled);

        await Assert.That(compiled.Limit).IsEqualTo(native.Limit);
        await Assert.That(compiled.Limit).IsNull();
    }

    [Test]
    public async Task A_reload_keeps_what_the_control_put_in_a_one_way_slot()
    {
        NoesisRuntime.Start();

        var native = ClickedThenReloaded(box =>
            box.SetBinding(
                ToggleButton.IsCheckedProperty,
                new Binding(nameof(SpikeItem.Flag)) { Mode = BindingMode.OneWay }
            )
        );
        var compiled = ClickedThenReloaded(box =>
            CompiledBinding.Bind(
                box,
                ToggleButton.IsCheckedProperty,
                new CompiledBindingSpec { Hops = [Hop.Flag], Mode = BindingMode.OneWay }
            )
        );

        await Assert.That(compiled).IsEqualTo(native);
        await Assert.That(compiled).IsEqualTo(true);
    }

    [Test]
    public async Task A_reload_after_a_collection_keeps_what_the_control_put_in_a_one_way_slot()
    {
        NoesisRuntime.Start();

        var source = new Border();
        Paint(source);
        var target = new Border { DataContext = source };
        CompiledBinding.Bind(
            target,
            Border.BorderBrushProperty,
            new CompiledBindingSpec
            {
                Hops =
                [
                    new BindingHop(nameof(Border.Background), static o => ((Border)o).Background),
                ],
                Mode = BindingMode.OneWay,
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, target);
        var placed = new SolidColorBrush(Colors.Blue);
        target.BorderBrush = placed;

        root.Children.Clear();
        NoesisRuntime.Pump(view, root);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        root.Children.Add(target);
        NoesisRuntime.Pump(view, root);

        await Assert.That(target.BorderBrush).IsSameReferenceAs(placed);
    }

    // Inlined, a stack slot would keep the brush alive and the test could not fail.
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    static void Paint(Border source) => source.Background = new SolidColorBrush(Colors.Red);

    [Test]
    public async Task A_two_way_edit_made_while_unloaded_reaches_the_source_on_reload()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Label = "orig" };
        var nativeBox = new TextBox { DataContext = native };
        nativeBox.SetBinding(
            TextBox.TextProperty,
            new Binding(nameof(SpikeItem.Label))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            }
        );

        var compiled = new SpikeItem { Label = "orig" };
        var compiledBox = new TextBox { DataContext = compiled };
        CompiledBinding.Bind(
            compiledBox,
            TextBox.TextProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Label],
                Write = (o, v) => ((SpikeItem)o).Label = (string)v!,
                Mode = BindingMode.TwoWay,
                Trigger = UpdateSourceTrigger.PropertyChanged,
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, nativeBox, compiledBox);

        root.Children.Clear();
        NoesisRuntime.Pump(view, root);
        nativeBox.Text = "typed";
        compiledBox.Text = "typed";
        root.Children.Add(nativeBox);
        root.Children.Add(compiledBox);
        NoesisRuntime.Pump(view, root);

        await Assert.That(compiled.Label).IsEqualTo(native.Label);
        await Assert.That(compiledBox.Text).IsEqualTo(nativeBox.Text);
        await Assert.That(compiled.Label).IsEqualTo("typed");
    }

    [Test]
    public async Task A_reload_writes_nothing_back_to_a_two_way_source_nobody_edited()
    {
        NoesisRuntime.Start();

        var converter = new UpperConverter();
        var native = new SpikeItem { Label = "Mixed" };
        var nativeBox = new TextBox { DataContext = native };
        nativeBox.SetBinding(
            TextBox.TextProperty,
            new Binding(nameof(SpikeItem.Label))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = converter,
            }
        );

        var compiled = new SpikeItem { Label = "Mixed" };
        var compiledBox = new TextBox { DataContext = compiled };
        CompiledBinding.Bind(
            compiledBox,
            TextBox.TextProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Label],
                Write = (o, v) => ((SpikeItem)o).Label = (string)v!,
                Mode = BindingMode.TwoWay,
                Trigger = UpdateSourceTrigger.PropertyChanged,
                Converter = converter,
                TargetType = typeof(string),
                SourceType = typeof(string),
                Convert = v => v as string ?? "",
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, nativeBox, compiledBox);

        root.Children.Clear();
        NoesisRuntime.Pump(view, root);
        root.Children.Add(nativeBox);
        root.Children.Add(compiledBox);
        NoesisRuntime.Pump(view, root);

        await Assert.That(native.Label).IsEqualTo("Mixed");
        await Assert.That(compiled.Label).IsEqualTo(native.Label);
    }

    [Test]
    public async Task A_reload_after_a_collection_writes_nothing_back_to_a_two_way_source_nobody_edited()
    {
        NoesisRuntime.Start();

        var writes = 0;
        var item = new SpikeItem { Label = "red" };
        var host = new Border { DataContext = item };
        CompiledBinding.Bind(
            host,
            Border.BackgroundProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Label],
                Write = (o, v) =>
                {
                    writes++;
                    ((SpikeItem)o).Label = (string)v!;
                },
                Mode = BindingMode.TwoWay,
                Trigger = UpdateSourceTrigger.PropertyChanged,
                Converter = new FreshBrushConverter(),
                TargetType = typeof(Brush),
                SourceType = typeof(string),
                Convert = v => v as Brush,
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, host);

        root.Children.Clear();
        NoesisRuntime.Pump(view, root);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        root.Children.Add(host);
        NoesisRuntime.Pump(view, root);

        await Assert.That(writes).IsEqualTo(0);
        await Assert.That(item.Label).IsEqualTo("red");
    }

    static bool? ClickedThenReloaded(Action<CheckBox> bind)
    {
        var box = new CheckBox
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            DataContext = new SpikeItem(),
        };
        bind(box);

        var root = new Grid { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, box);
        view.Activate();
        view.MouseButtonDown(10, 10, MouseButton.Left);
        view.MouseButtonUp(10, 10, MouseButton.Left);
        NoesisRuntime.Pump(view, root);

        root.Children.Remove(box);
        NoesisRuntime.Pump(view, root);
        root.Children.Add(box);
        NoesisRuntime.Pump(view, root);
        return box.IsChecked;
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
