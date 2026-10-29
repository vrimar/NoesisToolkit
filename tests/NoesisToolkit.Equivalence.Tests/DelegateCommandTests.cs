using System.Windows.Input;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class DelegateCommandTests
{
    [Test]
    public async Task A_null_parameter_is_refused_only_where_the_type_cannot_hold_it()
    {
        var ran = false;
        ICommand count = new DelegateCommand<int>(_ => ran = true);
        ICommand optional = new DelegateCommand<int?>(_ => ran = true);

        await Assert.That(count.CanExecute(null)).IsFalse();
        Assert.Throws<ArgumentException>(() => count.Execute(null));
        await Assert.That(ran).IsFalse();

        await Assert.That(optional.CanExecute(null)).IsTrue();
        optional.Execute(null);
        await Assert.That(ran).IsTrue();
    }
}
