using System;
using System.Threading.Tasks;

namespace NoesisToolkit.Mvvm;

/// <summary>
/// A command whose handler is asynchronous. It refuses re-entry until the handler completes, so a
/// second click cannot issue the same request twice, and reports that through
/// <see cref="ICommand.CanExecute"/> — which is what greys a bound button while the call is in flight.
/// </summary>
public sealed class AsyncDelegateCommand : AsyncDelegateCommand<object?>
{
    /// <summary>Creates a command from an asynchronous handler.</summary>
    /// <param name="execute">Runs when the command is invoked.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public AsyncDelegateCommand(Func<ValueTask> execute)
        : base(_ => execute())
    {
        Guard.NotNull(execute, nameof(execute));
    }
}

/// <inheritdoc cref="AsyncDelegateCommand"/>
/// <typeparam name="T">The command parameter type.</typeparam>
public class AsyncDelegateCommand<T> : ICommand
{
    private readonly Func<T?, ValueTask> _execute;

    private bool _running;

    /// <summary>Raised when <see cref="CanExecute"/> may have changed.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>Creates a command from an asynchronous handler.</summary>
    /// <param name="execute">Runs when the command is invoked.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public AsyncDelegateCommand(Func<T?, ValueTask> execute)
    {
        _execute = Guard.NotNull(execute, nameof(execute));
    }

    /// <summary>False while a previous invocation is still in flight.</summary>
    public bool CanExecute(T? parameter) => !_running;

    /// <summary>Runs the command, unless a previous invocation is still in flight.</summary>
    // Re-checked here, not only in CanExecute: a caller may invoke Execute without consulting it.
    public void Execute(T? parameter)
    {
        if (_running)
            return;

        _running = true;
        RaiseCanExecuteChanged();
        Forget(Run(parameter));
    }

    bool ICommand.CanExecute(object? parameter) =>
        DelegateCommand<T>.Fits(parameter) && CanExecute(DelegateCommand<T>.Cast(parameter));

    void ICommand.Execute(object? parameter) => Execute(DelegateCommand<T>.Cast(parameter));

    /// <summary>Tells bound controls to re-query <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    // Nothing awaits this, so a discarded ValueTask would swallow the fault.
    private static async void Forget(ValueTask running)
    {
        await running;
    }

    private async ValueTask Run(T? parameter)
    {
        try
        {
            await _execute(parameter);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }
}
