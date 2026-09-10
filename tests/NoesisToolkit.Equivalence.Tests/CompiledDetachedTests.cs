using Noesis;
using Noesis.Interactivity;
using NoesisToolkit.Mvvm;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

// A compiled binding whose receiver is an attached object with no lifetime of its own, judged
// against the native binding the interactivity engine resolves through inheritance context.
[NotInParallel("Noesis")]
public sealed class CompiledDetachedTests
{
    public sealed class SpikeBehavior : Behavior<FrameworkElement>
    {
        public static readonly DependencyProperty PayloadProperty = DependencyProperty.Register(
            "Payload",
            typeof(string),
            typeof(SpikeBehavior),
            new PropertyMetadata("untouched")
        );

        public string? Payload
        {
            get => (string?)GetValue(PayloadProperty);
            set => SetValue(PayloadProperty, value);
        }
    }

    public sealed class SpikeAction : TriggerAction<FrameworkElement>
    {
        public static readonly DependencyProperty ParameterProperty = DependencyProperty.Register(
            "Parameter",
            typeof(object),
            typeof(SpikeAction),
            new PropertyMetadata(null)
        );

        public object? Parameter
        {
            get => GetValue(ParameterProperty);
            set => SetValue(ParameterProperty, value);
        }

        protected override void Invoke(object parameter) { }
    }

    public sealed class SpikeTrigger : TriggerBase<FrameworkElement> { }

    [Test]
    public async Task A_behavior_property_follows_the_hosts_data_context_as_the_native_binding_does()
    {
        NoesisRuntime.Start();

        var native = new SpikeItem { Label = "start" };
        var compiled = new SpikeItem { Label = "start" };

        var nativeHost = new StackPanel { DataContext = native };
        var nativeBehavior = new SpikeBehavior();
        Interaction.GetBehaviors(nativeHost).Add(nativeBehavior);
        BindingOperations.SetBinding(
            nativeBehavior,
            SpikeBehavior.PayloadProperty,
            new Binding(nameof(SpikeItem.Label))
        );

        var compiledHost = new StackPanel { DataContext = compiled };
        Interaction.GetBehaviors(compiledHost).Add(new SpikeBehavior());
        CompiledBinding.Bind(
            compiledHost,
            host => CompiledBinding.BehaviorAt(host, 0),
            SpikeBehavior.PayloadProperty,
            new CompiledBindingSpec { Hops = [Hop.Label] }
        );

        NoesisRuntime.Show(nativeHost, compiledHost);
        var compiledBehavior = (SpikeBehavior)Interaction.GetBehaviors(compiledHost)[0];
        await Assert.That(compiledBehavior.Payload).IsEqualTo("start");
        await Assert
            .That(compiledBehavior.Payload)
            .IsEqualTo(((SpikeBehavior)Interaction.GetBehaviors(nativeHost)[0]).Payload);

        native.Label = "moved";
        compiled.Label = "moved";
        await Assert.That(compiledBehavior.Payload).IsEqualTo("moved");
        await Assert
            .That(compiledBehavior.Payload)
            .IsEqualTo(((SpikeBehavior)Interaction.GetBehaviors(nativeHost)[0]).Payload);
    }

    [Test]
    public async Task An_action_parameter_takes_the_data_context_itself()
    {
        NoesisRuntime.Start();

        var item = new SpikeItem { Label = "held" };

        var host = new StackPanel { DataContext = item };
        var trigger = new SpikeTrigger();
        trigger.Actions.Add(new SpikeAction());
        Interaction.GetTriggers(host).Add(trigger);

        CompiledBinding.Bind(
            host,
            h => CompiledBinding.ActionAt(h, 0, 0),
            SpikeAction.ParameterProperty,
            new CompiledBindingSpec { Hops = [] }
        );

        NoesisRuntime.Show(host);
        var action = (SpikeAction)
            ((Noesis.Interactivity.TriggerBase)Interaction.GetTriggers(host)[0]).Actions[0];
        await Assert.That(action.Parameter).IsSameReferenceAs(item);

        var swapped = new SpikeItem();
        host.DataContext = swapped;
        await Assert.That(action.Parameter).IsSameReferenceAs(swapped);
    }

    [Test]
    public async Task A_receiver_that_never_arrives_writes_nothing()
    {
        NoesisRuntime.Start();

        var host = new StackPanel { DataContext = new SpikeItem { Label = "x" } };
        var behavior = new SpikeBehavior { Payload = "untouched" };
        Interaction.GetBehaviors(host).Add(behavior);

        // Index 5 names a behavior that is not there, so the binding has nowhere to write.
        CompiledBinding.Bind(
            host,
            h => CompiledBinding.BehaviorAt(h, 5),
            SpikeBehavior.PayloadProperty,
            new CompiledBindingSpec { Hops = [Hop.Label] }
        );

        NoesisRuntime.Show(host);

        await Assert.That(behavior.Payload).IsEqualTo("untouched");
    }
}
