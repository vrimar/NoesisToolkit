using System.Windows.Input;
using Noesis;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests;

[NotInParallel("Noesis")]
public sealed class DelegateCommandTests
{
    [Test]
    public async Task A_null_parameter_runs_only_where_the_type_can_hold_it()
    {
        var ran = false;
        ICommand count = new DelegateCommand<int>(_ => ran = true);
        ICommand optional = new DelegateCommand<int?>(_ => ran = true);

        Assert.Throws<ArgumentException>(() => count.Execute(null));
        await Assert.That(ran).IsFalse();

        await Assert.That(optional.CanExecute(null)).IsTrue();
        optional.Execute(null);
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task A_value_typed_command_set_before_its_parameter_leaves_the_button_enabled()
    {
        NoesisRuntime.Start();

        ICommand[] commands =
        [
            new DelegateCommand<int>(_ => { }),
            new AsyncDelegateCommand<int>(_ => default),
        ];

        var enabled = new List<bool>();
        foreach (var command in commands)
        {
            var button = new Button { Command = command };
            var view = NoesisRuntime.Show(button);
            button.CommandParameter = 1;
            NoesisRuntime.Pump(view, button);
            enabled.Add(button.IsEnabled);
        }

        await Assert.That(enabled).IsEquivalentTo([true, true]);
    }
}
