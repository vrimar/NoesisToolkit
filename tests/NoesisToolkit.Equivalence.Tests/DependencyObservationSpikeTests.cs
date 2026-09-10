using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// The terms on which a DependencyProperty can be observed at all. OverrideMetadata is process-global
// and has no uninstall, so every hook here is permanent and one test holds the whole run: the answers
// depend on the order the pairs were claimed in.
[NotInParallel("Noesis")]
public sealed class DependencyObservationSpikeTests
{
    [Test]
    public async Task What_an_overridden_callback_reports()
    {
        NoesisRuntime.Start();
        Overrides.Install();

        var loose = new TextBlock();
        Overrides.Seen.Clear();
        loose.Tag = "detached";

        // Nothing outside a live view reports, so a watcher only settles once the tree is shown.
        await Assert.That(Overrides.Seen).IsEmpty();
        await Assert.That(loose.Tag).IsEqualTo("detached");

        var root = new StackPanel { Width = 400, Height = 300 };
        var text = new TextBlock();
        root.Children.Add(text);

        var view = GUI.CreateView(root);
        view.SetSize(400, 300);
        Pump(view, root);

        Overrides.Seen.Clear();
        text.Tag = "tagged";
        await Assert.That(Overrides.Seen).IsEquivalentTo(["FrameworkElement.Tag -> tagged"]);

        // The args carry the same instance the static field does, so a table can key on it.
        await Assert.That(Overrides.LastProperty).IsSameReferenceAs(FrameworkElement.TagProperty);

        Overrides.Seen.Clear();
        var source = new SpikeItem { Label = "bound" };
        text.DataContext = source;
        text.SetBinding(FrameworkElement.TagProperty, new Binding(nameof(SpikeItem.Label)));
        Pump(view, root);
        await Assert.That(Overrides.Seen).Contains("FrameworkElement.Tag -> bound");

        Overrides.Seen.Clear();
        source.Label = "pushed";
        Pump(view, root);
        await Assert.That(Overrides.Seen).Contains("FrameworkElement.Tag -> pushed");

        var loser = new List<string>();
        FrameworkElement.TagProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new PropertyMetadata((_, e) => loser.Add($"{e.NewValue}"))
        );
        Overrides.Seen.Clear();
        text.Tag = "second";

        // The last claim on a pair wins, so anything overriding after us silently takes the hook.
        await Assert.That(Overrides.Seen).IsEmpty();
        await Assert.That(loser).IsEquivalentTo(["second"]);

        // And nothing reads back, so that loss cannot be detected either.
        await Assert
            .That(
                FrameworkElement
                    .TagProperty.GetMetadata(typeof(FrameworkElement))
                    .PropertyChangedCallback
            )
            .IsNull();
    }

    [Test]
    public async Task What_an_override_leaves_alone()
    {
        NoesisRuntime.Start();
        Overrides.Install();

        var root = new StackPanel { Width = 400, Height = 300 };
        var derived = new SpikeDerived();
        var owns = new SpikeOwnCallback();
        var thrower = new SpikeThrower();
        root.Children.Add(derived);
        root.Children.Add(owns);
        root.Children.Add(thrower);

        var view = GUI.CreateView(root);
        view.SetSize(400, 300);
        Pump(view, root);

        // The override stated a callback and no default, so the base default survived it.
        await Assert.That(derived.Kept).IsEqualTo("base-default");
        await Assert.That(owns.Mine).IsEqualTo("own-default");

        Overrides.Seen.Clear();
        derived.Kept = "set";
        await Assert
            .That(Overrides.Seen)
            .IsEquivalentTo(["SpikeBase.Kept -> set", "SpikeDerived.Kept -> set"]);

        Overrides.Seen.Clear();
        SpikeOwnCallback.Own.Clear();
        owns.Mine = "written";

        // Claiming the type that registered the property leaves its own callback running.
        await Assert.That(SpikeOwnCallback.Own).IsEquivalentTo(["written"]);
        await Assert.That(Overrides.Seen).IsEquivalentTo(["SpikeOwnCallback.Mine -> written"]);

        // Swallowed at the interop boundary, so a handler must never rely on throwing.
        // Swallowed at the interop boundary: this must not throw.
        thrower.Boom = "kaboom";
        await Assert.That(thrower.Boom).IsEqualTo("kaboom");
    }

    [Test]
    public async Task Framework_metadata_answers_for_direction_and_trigger()
    {
        NoesisRuntime.Start();

        await Assert
            .That(Metadata(TextBox.TextProperty, typeof(TextBox)))
            .IsEqualTo((true, UpdateSourceTrigger.LostFocus));
        await Assert
            .That(Metadata(ToggleButton.IsCheckedProperty, typeof(CheckBox)))
            .IsEqualTo((true, UpdateSourceTrigger.PropertyChanged));
        await Assert
            .That(Metadata(Selector.SelectedIndexProperty, typeof(ComboBox)))
            .IsEqualTo((true, UpdateSourceTrigger.PropertyChanged));
        await Assert
            .That(Metadata(TextBlock.TextProperty, typeof(TextBlock)))
            .IsEqualTo((false, UpdateSourceTrigger.PropertyChanged));
    }

    static (bool TwoWay, UpdateSourceTrigger Trigger) Metadata(
        DependencyProperty property,
        Type type
    )
    {
        var metadata = (FrameworkPropertyMetadata)property.GetMetadata(type);
        return (metadata.BindsTwoWayByDefault, metadata.DefaultUpdateSourceTrigger);
    }

    static void Pump(View view, FrameworkElement root)
    {
        for (var i = 0; i < 6; i++)
            view.Update(i * 0.016);

        root.UpdateLayout();
    }
}
