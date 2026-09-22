using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// What the compiler emits when the source type does not fit the slot, judged against the native
// binding it replaces.
[NotInParallel("Noesis")]
public sealed class CompiledConversionTests
{
    [Test]
    public async Task An_int_reaches_a_string_slot_as_a_native_binding_puts_it_there()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Index = 7 };
        var compiled = new SpikeItem { Index = 7 };

        var nativeText = new TextBlock { DataContext = native };
        nativeText.SetBinding(TextBlock.TextProperty, new Binding(nameof(SpikeItem.Index)));

        var compiledText = new TextBlock { DataContext = compiled };
        CompiledBinding.Bind(
            compiledText,
            TextBlock.TextProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Index],
                Convert = SlotConversion.Cached<int>(static t => SlotConversion.Text(t), null),
            }
        );

        NoesisRuntime.Show(nativeText, compiledText);
        await Assert.That(compiledText.Text).IsEqualTo("7");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);

        native.Index = 42;
        compiled.Index = 42;
        await Assert.That(compiledText.Text).IsEqualTo("42");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
    }

    [Test]
    public async Task A_null_source_leaves_a_string_slot_where_a_native_binding_leaves_it()
    {
        NoesisRuntime.Start();

        var nativeItem = new SpikeItem { Payload = "filled" };
        var compiledItem = new SpikeItem { Payload = "filled" };

        var nativeText = new TextBlock { DataContext = nativeItem };
        nativeText.SetBinding(TextBlock.TextProperty, new Binding(nameof(SpikeItem.Payload)));

        var compiledText = new TextBlock { DataContext = compiledItem };
        CompiledBinding.Bind(
            compiledText,
            TextBlock.TextProperty,
            new CompiledBindingSpec { Hops = [Hop.Payload], Convert = v => v?.ToString() }
        );

        NoesisRuntime.Show(nativeText, compiledText);

        // Written first, so going null is a change rather than a slot nothing ever touched.
        await Assert.That(compiledText.Text).IsEqualTo("filled");

        nativeItem.Payload = null;
        compiledItem.Payload = null;

        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
        await Assert.That(compiledText.Text).IsEqualTo("");
    }

    [Test]
    public async Task A_double_reaches_a_float_slot()
    {
        NoesisRuntime.Start();

        var source = new SpikeItem { Ratio = 12.5 };
        var target = new TextBlock { DataContext = source };
        CompiledBinding.Bind(
            target,
            FrameworkElement.WidthProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Ratio],
                Convert = SlotConversion.Cached<double>(
                    static t => (float)t,
                    DependencyProperty.UnsetValue
                ),
            }
        );

        NoesisRuntime.Show(target);
        await Assert.That(target.Width).IsEqualTo(12.5f);

        source.Ratio = 40;
        await Assert.That(target.Width).IsEqualTo(40f);
    }

    [Test]
    public async Task A_cached_conversion_converts_each_value_once()
    {
        var convert = SlotConversion.Cached<double>(
            static t => (float)t,
            DependencyProperty.UnsetValue
        );

        var first = convert(12.5);
        await Assert.That(first).IsEqualTo(12.5f);
        await Assert.That(ReferenceEquals(convert(12.5), first)).IsTrue();
        await Assert.That(ReferenceEquals(convert(40.0), first)).IsFalse();
        await Assert.That(convert(null)).IsSameReferenceAs(DependencyProperty.UnsetValue);
        await Assert.That(convert("12.5")).IsSameReferenceAs(DependencyProperty.UnsetValue);
    }

    [Test]
    public async Task An_int_reaches_a_corner_radius_slot_as_a_native_binding_puts_it_there()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Index = 6 };
        var compiled = new SpikeItem { Index = 6 };

        var nativeBorder = new Border { DataContext = native };
        nativeBorder.SetBinding(Border.CornerRadiusProperty, new Binding(nameof(SpikeItem.Index)));

        var compiledBorder = new Border { DataContext = compiled };
        CompiledBinding.Bind(
            compiledBorder,
            Border.CornerRadiusProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Index],
                Convert = v => v is int t ? (object)new CornerRadius(t) : default(CornerRadius),
            }
        );

        NoesisRuntime.Show(nativeBorder, compiledBorder);
        await Assert.That(compiledBorder.CornerRadius.TopLeft).IsEqualTo(6f);
        await Assert.That(compiledBorder.CornerRadius).IsEqualTo(nativeBorder.CornerRadius);

        native.Index = 11;
        compiled.Index = 11;
        await Assert.That(compiledBorder.CornerRadius.TopLeft).IsEqualTo(11f);
        await Assert.That(compiledBorder.CornerRadius).IsEqualTo(nativeBorder.CornerRadius);
    }

    [Test]
    public async Task An_int_reaches_a_grid_length_slot_as_a_native_binding_puts_it_there()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Index = 120 };
        var compiled = new SpikeItem { Index = 120 };

        var nativeColumn = new ColumnDefinition();
        var nativeGrid = new Grid { DataContext = native };
        nativeGrid.ColumnDefinitions.Add(nativeColumn);
        nativeColumn.SetBinding(
            ColumnDefinition.WidthProperty,
            new Binding(nameof(SpikeItem.Index))
        );

        var compiledColumn = new ColumnDefinition();
        var compiledGrid = new Grid { DataContext = compiled };
        compiledGrid.ColumnDefinitions.Add(compiledColumn);
        CompiledBinding.Bind(
            compiledColumn,
            ColumnDefinition.WidthProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Index],
                Convert = v => v is int t ? (object)new GridLength(t) : default(GridLength),
                Assign = (t, v) => ((ColumnDefinition)t).Width = v is GridLength g ? g : default,
            }
        );

        NoesisRuntime.Show(nativeGrid, compiledGrid);
        await Assert.That(compiledColumn.Width.Value).IsEqualTo(120f);
        await Assert.That(compiledColumn.Width.IsAbsolute).IsTrue();
        await Assert.That(compiledColumn.Width).IsEqualTo(nativeColumn.Width);

        native.Index = 48;
        compiled.Index = 48;
        await Assert.That(compiledColumn.Width.Value).IsEqualTo(48f);
        await Assert.That(compiledColumn.Width).IsEqualTo(nativeColumn.Width);
    }

    [Test]
    public async Task A_path_string_reaches_an_image_source_slot_as_a_native_binding_puts_it_there()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Label = "Fixtures/icon.png" };
        var compiled = new SpikeItem { Label = "Fixtures/icon.png" };

        var nativeImage = new Image { DataContext = native };
        nativeImage.SetBinding(Image.SourceProperty, new Binding(nameof(SpikeItem.Label)));

        var compiledImage = new Image { DataContext = compiled };
        CompiledBinding.Bind(
            compiledImage,
            Image.SourceProperty,
            new CompiledBindingSpec
            {
                Hops = [Hop.Label],
                Convert = v => v is string t ? ImageSources.From(t) : null,
            }
        );

        NoesisRuntime.Show(nativeImage, compiledImage);
        await Assert.That(SourcePath(compiledImage)).IsEqualTo("Fixtures/icon.png");
        await Assert.That(SourcePath(compiledImage)).IsEqualTo(SourcePath(nativeImage));

        native.Label = "";
        compiled.Label = "";
        await Assert.That(SourcePath(compiledImage)).IsEqualTo(SourcePath(nativeImage));
    }

    [Test]
    public async Task Rows_converting_the_same_path_share_one_image()
    {
        NoesisRuntime.Start();

        var first = ImageSources.From("Fixtures/icon.png");
        var second = ImageSources.From("Fixtures/icon.png");

        await Assert.That(second).IsSameReferenceAs(first);
        await Assert
            .That(((BitmapImage)first).UriSource.OriginalString)
            .IsEqualTo("Fixtures/icon.png");
    }

    static string? SourcePath(Image image) =>
        ((BitmapImage?)image.Source)?.UriSource.OriginalString;

    [Test]
    public async Task An_identity_binding_hands_over_the_data_context_itself()
    {
        NoesisRuntime.Start();

        var source = new SpikeItem { Label = "held" };
        var target = new TextBlock { DataContext = source };
        CompiledBinding.Bind(
            target,
            TextBlock.TagProperty,
            new CompiledBindingSpec { Hops = [], Convert = v => v }
        );

        NoesisRuntime.Show(target);
        await Assert.That(target.Tag).IsSameReferenceAs(source);

        var swapped = new SpikeItem { Label = "swapped" };
        target.DataContext = swapped;
        await Assert.That(target.Tag).IsSameReferenceAs(swapped);
    }
}
