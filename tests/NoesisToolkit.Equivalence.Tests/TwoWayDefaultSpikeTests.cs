using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// A compiled binding is one-way, and the compiler cannot read BindsTwoWayByDefault off a native
// DependencyProperty. If any of these bound two-way without saying so, compiling it would silently
// drop the write-back — so the properties most likely to are pinned here.
[NotInParallel("Noesis")]
public sealed class TwoWayDefaultSpikeTests
{
    [Test]
    [Arguments("TextBox.Text", false)]
    [Arguments("ToggleButton.IsChecked", true)]
    [Arguments("Selector.SelectedIndex", true)]
    public async Task Which_properties_bind_two_way_without_saying_so(string which, bool twoWay)
    {
        NoesisRuntime.Start();

        var (bareSource, bare, slot, write) = Target(which);
        var (explicitSource, explicitTarget, _, explicitWrite) = Target(which);

        bare.SetBinding(slot, new Binding(Path(which)));
        explicitTarget.SetBinding(
            slot,
            new Binding(Path(which))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            }
        );

        var root = new StackPanel { Width = 400, Height = 300 };
        root.Children.Add(bare);
        root.Children.Add(explicitTarget);

        var view = GUI.CreateView(root);
        view.SetSize(400, 300);
        for (var i = 0; i < 6; i++)
            view.Update(i * 0.016);
        root.UpdateLayout();

        write(bare);
        explicitWrite(explicitTarget);

        // The explicit side proves the harness observes a write-back at all.
        await Assert.That(Read(which, explicitSource)).IsEqualTo(Written(which));
        await Assert
            .That(Read(which, bareSource))
            .IsEqualTo(twoWay ? Written(which) : Initial(which));
    }

    static string Path(string which) =>
        which switch
        {
            "TextBox.Text" => nameof(SpikeItem.Label),
            "ToggleButton.IsChecked" => nameof(SpikeItem.Flag),
            _ => nameof(SpikeItem.Index),
        };

    static object Initial(string which) =>
        which switch
        {
            "TextBox.Text" => "before",
            "ToggleButton.IsChecked" => false,
            _ => 0,
        };

    static object Written(string which) =>
        which switch
        {
            "TextBox.Text" => "typed",
            "ToggleButton.IsChecked" => true,
            _ => 1,
        };

    static object? Read(string which, SpikeItem source) =>
        which switch
        {
            "TextBox.Text" => source.Label,
            "ToggleButton.IsChecked" => source.Flag,
            _ => source.Index,
        };

    static (
        SpikeItem Source,
        FrameworkElement Target,
        DependencyProperty Slot,
        Action<FrameworkElement> Write
    ) Target(string which)
    {
        var source = new SpikeItem { Label = "before" };
        switch (which)
        {
            case "TextBox.Text":
                return (
                    source,
                    new TextBox { DataContext = source },
                    TextBox.TextProperty,
                    e => ((TextBox)e).Text = "typed"
                );
            case "ToggleButton.IsChecked":
                return (
                    source,
                    new CheckBox { DataContext = source },
                    ToggleButton.IsCheckedProperty,
                    e => ((CheckBox)e).IsChecked = true
                );
            default:
                var box = new ComboBox { DataContext = source };
                box.Items.Add(new ComboBoxItem());
                box.Items.Add(new ComboBoxItem());
                return (
                    source,
                    box,
                    Selector.SelectedIndexProperty,
                    e => ((ComboBox)e).SelectedIndex = 1
                );
        }
    }
}
