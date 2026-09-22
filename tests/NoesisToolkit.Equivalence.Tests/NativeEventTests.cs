using Noesis;
using NoesisToolkit.Mvvm.CodeGen;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class NativeEventTests
{
    static CompiledBindingSpec LabelOf() =>
        new()
        {
            Hops = [new BindingHop(nameof(SpikeItem.Label), static o => ((SpikeItem)o).Label)],
        };

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_control_with_its_own_Loaded_handler_still_settles_its_binding(
        bool ownHandlerFirst
    )
    {
        NoesisRuntime.Start();

        var own = 0;
        var host = new SpikeControl { DataContext = new SpikeItem { Label = "settled" } };
        if (ownHandlerFirst)
            host.Loaded += (_, _) => own++;
        CompiledBinding.Bind(host, SpikeControl.LabelProperty, LabelOf());
        if (!ownHandlerFirst)
            host.Loaded += (_, _) => own++;

        NoesisRuntime.Show(host);

        await Assert.That(own).IsEqualTo(1);
        await Assert.That(host.Label).IsEqualTo("settled");
    }

    [Test]
    public async Task A_recycled_container_settles_under_its_new_context_and_lets_the_old_one_go()
    {
        NoesisRuntime.Start();

        var root = new StackPanel { Width = 400, Height = 300 };
        var host = new SpikeControl { DataContext = new SpikeItem { Label = "first" } };
        CompiledBinding.Bind(host, SpikeControl.LabelProperty, LabelOf());
        var view = NoesisRuntime.Show(root, host);
        await Assert.That(host.Label).IsEqualTo("first");

        root.Children.Remove(host);
        NoesisRuntime.Pump(view, root);

        var second = new SpikeItem { Label = "second" };
        host.DataContext = second;
        root.Children.Add(host);
        NoesisRuntime.Pump(view, root, host);

        await Assert.That(host.Label).IsEqualTo("second");

        second.Label = "changed";
        await Assert.That(host.Label).IsEqualTo("changed");
    }
}
