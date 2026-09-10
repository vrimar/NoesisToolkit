using System;

namespace NoesisToolkit.Mvvm;

/// <summary>A command that takes no parameter.</summary>
public sealed class DelegateCommand : DelegateCommand<object?>
{
    /// <summary>Creates a command that is always executable.</summary>
    /// <param name="execute">Runs when the command is invoked.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public DelegateCommand(Action execute)
        : base(_ => execute())
    {
        Guard.NotNull(execute, nameof(execute));
    }

    /// <summary>Creates a command gated by <paramref name="canExecute"/>.</summary>
    /// <param name="execute">Runs when the command is invoked.</param>
    /// <param name="canExecute">Decides whether the command is currently executable.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public DelegateCommand(Action execute, Func<bool> canExecute)
        : base(_ => execute(), _ => canExecute())
    {
        Guard.NotNull(execute, nameof(execute));
        Guard.NotNull(canExecute, nameof(canExecute));
    }
}

/// <summary>A command whose handler takes a parameter of type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The command parameter type.</typeparam>
public class DelegateCommand<T> : ICommand
{
    private readonly Func<T?, bool>? _canExecute;
    private readonly Action<T?> _execute;

    /// <summary>Raised when <see cref="CanExecute"/> may have changed.</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>Creates a command that is always executable.</summary>
    /// <param name="execute">Runs when the command is invoked.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public DelegateCommand(Action<T?> execute)
    {
        _execute = Guard.NotNull(execute, nameof(execute));
    }

    /// <summary>Creates a command gated by <paramref name="canExecute"/>.</summary>
    /// <param name="execute">Runs when the command is invoked.</param>
    /// <param name="canExecute">Decides whether the command is currently executable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="execute"/> is null.</exception>
    public DelegateCommand(Action<T?> execute, Func<T?, bool>? canExecute)
    {
        _execute = Guard.NotNull(execute, nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>Whether the command can run with this parameter.</summary>
    public bool CanExecute(T? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>Runs the command.</summary>
    public void Execute(T? parameter) => _execute(parameter);

    bool ICommand.CanExecute(object? parameter) => Fits(parameter) && CanExecute(Cast(parameter));

    void ICommand.Execute(object? parameter) => Execute(Cast(parameter));

    /// <summary>Tells bound controls to re-query <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Whether the parameter is one this command could take. A re-query has to answer,
    /// so a mismatch is "no", where invoking it is still a fault.</summary>
    internal static bool Fits(object? parameter) => parameter is null or T;

    internal static T? Cast(object? parameter)
    {
        if (parameter is null)
            return default;

        if (parameter is T t)
            return t;

        throw new ArgumentException(
            $"Invalid command parameter type. Expected {typeof(T).FullName}, got {parameter.GetType().FullName}."
        );
    }
}
