using System.Windows.Input;

namespace JobTracker.Presentation.Commands;

public sealed class UiCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class UiCommand<T>(Action<T> execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => parameter is T && (canExecute?.Invoke() ?? true);
    public void Execute(object? parameter) { if (parameter is T value && CanExecute(parameter)) execute(value); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
