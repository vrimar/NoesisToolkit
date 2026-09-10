using Noesis;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// A FindAncestor binding wired before its element has any parent, judged against the native one
// once the tree connects.
[NotInParallel("Noesis")]
public sealed class CompiledLateAncestorTests
{
    public sealed class SpikeHost : ContentControl
    {
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            "Label",
            typeof(string),
            typeof(SpikeHost),
            new PropertyMetadata("host-default")
        );

        public string? Label
        {
            get => (string?)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
            "Count",
            typeof(int),
            typeof(SpikeHost),
            new PropertyMetadata(1)
        );

        public int Count
        {
            get => (int)GetValue(CountProperty);
            set => SetValue(CountProperty, value);
        }
    }

    [Test]
    public async Task A_panel_clone_finds_its_ancestor_as_the_native_binding_does()
    {
        NoesisRuntime.Start();

        static ItemsControl BuildCompiled()
        {
            var proto = new UniformGrid();
            CompiledBindingSetup.SetIndex(
                proto,
                CompiledBindingSetup.Register(
                    "spike#panel",
                    __e =>
                        CompiledBinding.Bind(
                            __e,
                            UniformGrid.RowsProperty,
                            new CompiledBindingSpec
                            {
                                Source = s => CompiledBinding.FindAncestor(s, typeof(SpikeHost)),
                                SourceProperty = SpikeHost.CountProperty,
                                Hops = [],
                                Convert = v => (object)(v is int t ? t : default(int)),
                                Assign = (t, v) =>
                                    ((UniformGrid)t).Rows = v is int w ? w : default(int),
                            }
                        )
                )
            );

            var template = new ItemsPanelTemplate { VisualTree = proto };
            return new ItemsControl { ItemsPanel = template };
        }

        static ItemsControl BuildNative()
        {
            var proto = new UniformGrid();
            proto.SetBinding(
                UniformGrid.RowsProperty,
                new Binding("Count")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.FindAncestor,
                        typeof(SpikeHost),
                        1
                    ),
                }
            );

            var template = new ItemsPanelTemplate { VisualTree = proto };
            return new ItemsControl { ItemsPanel = template };
        }

        var nativeItems = BuildNative();
        var compiledItems = BuildCompiled();
        var nativeHost = new SpikeHost { Count = 4, Content = nativeItems };
        var compiledHost = new SpikeHost { Count = 4, Content = compiledItems };

        NoesisRuntime.Show(nativeHost, compiledHost);

        static int Rows(ItemsControl items)
        {
            for (
                DependencyObject? node = items;
                node is not null;
                node = Descend(node, out node) ? node : null
            )
            {
                if (node is UniformGrid grid)
                    return grid.Rows;
            }

            return int.MinValue;
        }

        static bool Descend(DependencyObject node, out DependencyObject next)
        {
            next = node;
            if (VisualTreeHelper.GetChildrenCount(node) == 0)
                return false;

            next = VisualTreeHelper.GetChild(node, 0);
            return true;
        }

        await Assert.That(Rows(compiledItems)).IsEqualTo(4);
        await Assert.That(Rows(compiledItems)).IsEqualTo(Rows(nativeItems));
    }

    [Test]
    public async Task An_ancestor_that_arrives_after_binding_is_found_and_followed()
    {
        NoesisRuntime.Start();

        var nativeText = new TextBlock();
        nativeText.SetBinding(
            TextBlock.TextProperty,
            new Binding("Label")
            {
                RelativeSource = new RelativeSource(
                    RelativeSourceMode.FindAncestor,
                    typeof(SpikeHost),
                    1
                ),
            }
        );

        var compiledText = new TextBlock();
        CompiledBinding.Bind(
            compiledText,
            TextBlock.TextProperty,
            new CompiledBindingSpec
            {
                Source = s => CompiledBinding.FindAncestor(s, typeof(SpikeHost)),
                SourceProperty = SpikeHost.LabelProperty,
                Hops = [],
                Convert = v => v as string,
            }
        );

        var nativeHost = new SpikeHost { Label = "found", Content = nativeText };
        var compiledHost = new SpikeHost { Label = "found", Content = compiledText };

        NoesisRuntime.Show(nativeHost, compiledHost);
        await Assert.That(compiledText.Text).IsEqualTo("found");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);

        nativeHost.Label = "moved";
        compiledHost.Label = "moved";
        await Assert.That(compiledText.Text).IsEqualTo("moved");
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
    }

    [Test]
    public async Task An_ancestor_that_never_arrives_leaves_the_slot_alone()
    {
        NoesisRuntime.Start();

        var nativeText = new TextBlock();
        nativeText.SetBinding(
            TextBlock.TextProperty,
            new Binding("Label")
            {
                RelativeSource = new RelativeSource(
                    RelativeSourceMode.FindAncestor,
                    typeof(SpikeHost),
                    1
                ),
            }
        );

        var compiledText = new TextBlock();
        CompiledBinding.Bind(
            compiledText,
            TextBlock.TextProperty,
            new CompiledBindingSpec
            {
                Source = s => CompiledBinding.FindAncestor(s, typeof(SpikeHost)),
                SourceProperty = SpikeHost.LabelProperty,
                Hops = [],
                Convert = v => v as string,
            }
        );

        NoesisRuntime.Show(nativeText, compiledText);

        // Both sides land on TextBlock.Text's default, so the absolute is what says the slot is
        // untouched rather than merely equal to another untouched slot.
        await Assert.That(compiledText.Text).IsEqualTo(nativeText.Text);
        await Assert.That(compiledText.Text).IsEqualTo("");
    }
}
