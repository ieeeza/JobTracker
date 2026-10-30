using System.Windows.Input;

namespace JobTracker.Presentation.Commands;

/// <summary>An awaitable command; its delegate supplies the presentation error boundary.</summary>
public sealed class AsyncUiCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => _ = ExecuteAsync();
    public Task ExecuteAsync() => CanExecute(null) ? execute() : Task.CompletedTask;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
