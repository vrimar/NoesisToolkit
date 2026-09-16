using System.Globalization;
using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// A compiled MultiBinding judged against the native one it replaces.
[NotInParallel("Noesis")]
public sealed class CompiledMultiBindingTests
{
    // Renders raw values, not types: this is judged against what the native engine puts in the slot.
    sealed class Join : IMultiValueConverter
    {
        public object? Convert(
            object[] values,
            Type targetType,
            object parameter,
            CultureInfo culture
        ) =>
            string.Join(
                (string?)parameter ?? ",",
                values.Select(v => ReferenceEquals(v, DependencyProperty.UnsetValue) ? "?" : $"{v}")
            );

        public object[] ConvertBack(
            object value,
            Type[] targetTypes,
            object parameter,
            CultureInfo culture
        ) => throw new NotSupportedException();
    }

    [Test]
    public async Task It_survives_being_unloaded_and_loaded_again()
    {
        NoesisRuntime.Start();

        var source = new SpikeItem { Label = "a", Index = 1 };
        var target = new TextBlock { DataContext = source };
        CompiledMultiBinding.Bind(
            target,
            TextBlock.TextProperty,
            new CompiledMultiBindingSpec
            {
                Parts =
                [
                    new CompiledBindingPart { Hops = [Hop.Label] },
                    new CompiledBindingPart { Hops = [Hop.Index] },
                ],
                Converter = new Join(),
                ConverterParameter = "-",
                TargetType = typeof(string),
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, target);
        await Assert.That(target.Text).IsEqualTo("a-1");

        // A virtualizing panel recycles containers exactly this way.
        root.Children.Remove(target);
        NoesisRuntime.Pump(view, root);
        root.Children.Add(target);
        NoesisRuntime.Pump(view, root);

        source.Label = "b";
        await Assert.That(target.Text).IsEqualTo("b-1");
    }

    [Test]
    public async Task A_part_whose_source_arrives_late_is_found_and_followed()
    {
        NoesisRuntime.Start();

        var target = new TextBlock { DataContext = new SpikeItem { Label = "own" } };
        CompiledMultiBinding.Bind(
            target,
            TextBlock.TextProperty,
            new CompiledMultiBindingSpec
            {
                Parts =
                [
                    new CompiledBindingPart { Hops = [Hop.Label] },
                    new CompiledBindingPart
                    {
                        Source = s =>
                            CompiledBinding.FindAncestor(
                                s,
                                typeof(CompiledLateAncestorTests.SpikeHost)
                            ),
                        SourceProperty = CompiledLateAncestorTests.SpikeHost.LabelProperty,
                        Hops = [],
                    },
                ],
                Converter = new Join(),
                ConverterParameter = "-",
                TargetType = typeof(string),
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        var view = NoesisRuntime.Show(root, target);

        // The ancestor is not there yet, so the part holds nothing.
        await Assert.That(target.Text).IsEqualTo("own-?");

        root.Children.Remove(target);
        var host = new CompiledLateAncestorTests.SpikeHost { Label = "host" };
        host.Content = target;
        root.Children.Add(host);
        NoesisRuntime.Pump(view, root);

        await Assert.That(target.Text).IsEqualTo("own-host");
    }

    [Test]
    public async Task A_converter_multi_binding_tracks_both_sources_as_the_native_one_does()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Label = "a", Index = 1 };
        var compiled = new SpikeItem { Label = "a", Index = 1 };

        var nativeText = new TextBlock { DataContext = native };
        var multi = new MultiBinding { Converter = new Join(), ConverterParameter = "-" };
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Label)));
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Index)));
        BindingOperations.SetBinding(nativeText, TextBlock.TextProperty, multi);

        var compiledText = new TextBlock { DataContext = compiled };
        CompiledMultiBinding.Bind(
            compiledText,
            TextBlock.TextProperty,
            new CompiledMultiBindingSpec
            {
                Parts =
                [
                    new CompiledBindingPart { Hops = [Hop.Label] },
                    new CompiledBindingPart { Hops = [Hop.Index] },
                ],
                Converter = new Join(),
                ConverterParameter = "-",
                TargetType = typeof(string),
                Convert = v => v as string,
            }
        );

        NoesisRuntime.Show(nativeText, compiledText);
        await Assert.That(compiledText.Text).IsEqualTo("a-1");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);

        native.Index = 5;
        compiled.Index = 5;
        await Assert.That(compiledText.Text).IsEqualTo("a-5");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);

        native.Label = "b";
        compiled.Label = "b";
        await Assert.That(compiledText.Text).IsEqualTo("b-5");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
    }

    [Test]
    public async Task A_broken_part_reaches_the_converter_as_the_native_engine_hands_it_over()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Label = "a" };
        var compiled = new SpikeItem { Label = "a" };

        var nativeText = new TextBlock { DataContext = native };
        var multi = new MultiBinding { Converter = new Join() };
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Label)));
        multi.Bindings.Add(new Binding("Payload.Index"));
        BindingOperations.SetBinding(nativeText, TextBlock.TextProperty, multi);

        var compiledText = new TextBlock { DataContext = compiled };
        CompiledMultiBinding.Bind(
            compiledText,
            TextBlock.TextProperty,
            new CompiledMultiBindingSpec
            {
                Parts =
                [
                    new CompiledBindingPart { Hops = [Hop.Label] },
                    new CompiledBindingPart
                    {
                        Hops =
                        [
                            Hop.Payload,
                            BindingHop.Guarded<SpikeItem>(nameof(SpikeItem.Index), s => s.Index),
                        ],
                    },
                ],
                Converter = new Join(),
                TargetType = typeof(string),
                Convert = v => v as string,
            }
        );

        NoesisRuntime.Show(nativeText, compiledText);
        await Assert.That(compiledText.Text).IsEqualTo("a,?");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);

        native.Payload = new SpikeItem { Index = 3 };
        compiled.Payload = new SpikeItem { Index = 3 };
        await Assert.That(compiledText.Text).IsEqualTo("a,3");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
    }

    [Test]
    public async Task A_null_root_reaches_the_converter_as_the_native_engine_hands_it_over()
    {
        NoesisRuntime.Start();

        var nativeText = new TextBlock();
        var multi = new MultiBinding { Converter = new Join() };
        multi.Bindings.Add(new Binding());
        multi.Bindings.Add(
            new Binding(nameof(FrameworkElement.Tag))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.Self),
            }
        );
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Label)));
        BindingOperations.SetBinding(nativeText, TextBlock.TextProperty, multi);

        var compiledText = new TextBlock();
        CompiledMultiBinding.Bind(
            compiledText,
            TextBlock.TextProperty,
            new CompiledMultiBindingSpec
            {
                Parts =
                [
                    new CompiledBindingPart(),
                    new CompiledBindingPart
                    {
                        Source = e => e,
                        SourceProperty = FrameworkElement.TagProperty,
                    },
                    new CompiledBindingPart { Hops = [Hop.Label] },
                ],
                Converter = new Join(),
                TargetType = typeof(string),
                Convert = v => v as string,
            }
        );

        NoesisRuntime.Show(nativeText, compiledText);
        await Assert.That(nativeText.Text).IsEqualTo(",,?");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
    }

    [Test]
    public async Task A_string_format_multi_binding_lands_where_the_native_one_lands()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Index = 2 };
        var compiled = new SpikeItem { Index = 2 };

        var nativeHost = new SpikeControl { DataContext = native };
        var multi = new MultiBinding { StringFormat = "{0}/{1}" };
        multi.Bindings.Add(new Binding(nameof(SpikeItem.Index)));
        multi.Bindings.Add(new Binding("Payload.Index"));
        BindingOperations.SetBinding(nativeHost, SpikeControl.LabelProperty, multi);

        var compiledHost = new SpikeControl { DataContext = compiled };
        CompiledMultiBinding.Bind(
            compiledHost,
            SpikeControl.LabelProperty,
            new CompiledMultiBindingSpec
            {
                Parts =
                [
                    new CompiledBindingPart { Hops = [Hop.Index] },
                    new CompiledBindingPart
                    {
                        Hops =
                        [
                            Hop.Payload,
                            BindingHop.Guarded<SpikeItem>(nameof(SpikeItem.Index), s => s.Index),
                        ],
                    },
                ],
                Format = vs => string.Format(CultureInfo.CurrentCulture, "{0}/{1}", vs[0], vs[1]),
                Convert = v => v as string,
            }
        );

        NoesisRuntime.Show(nativeHost, compiledHost);
        await Assert.That(compiledHost.Label).IsEqualTo("host-default");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        native.Payload = new SpikeItem { Index = 8 };
        compiled.Payload = new SpikeItem { Index = 8 };
        await Assert.That(compiledHost.Label).IsEqualTo("2/8");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);

        native.Payload = null;
        compiled.Payload = null;
        await Assert.That(compiledHost.Label).IsEqualTo("host-default");
        await Assert.That(compiledHost.Label).IsEqualTo(nativeHost.Label);
    }

    [Test]
    public async Task A_data_context_swap_reevaluates_every_part()
    {
        NoesisRuntime.Start();

        var target = new TextBlock
        {
            DataContext = new SpikeItem { Label = "x", Index = 1 },
        };
        CompiledMultiBinding.Bind(
            target,
            TextBlock.TextProperty,
            new CompiledMultiBindingSpec
            {
                Parts =
                [
                    new CompiledBindingPart { Hops = [Hop.Label] },
                    new CompiledBindingPart { Hops = [Hop.Index] },
                ],
                Format = vs => string.Format(CultureInfo.CurrentCulture, "{0}#{1}", vs[0], vs[1]),
                Convert = v => v as string,
            }
        );

        NoesisRuntime.Show(target);
        await Assert.That(target.Text).IsEqualTo("x#1");

        target.DataContext = new SpikeItem { Label = "y", Index = 9 };
        await Assert.That(target.Text).IsEqualTo("y#9");
    }
}
