using System.Windows.Input;
using JobTracker.Presentation.Services;

namespace JobTracker.Presentation.Commands;

/// <summary>Runs a presentation operation through the shared error/cancellation boundary.</summary>
public sealed class AsyncUiCommand : ICommand
{
    private readonly UiOperationState _operations;
    private readonly Func<CancellationToken, Task> _execute;
    private readonly string _name;
    private readonly string _cancelLabel;
    private readonly Func<bool>? _canExecute;

    public AsyncUiCommand(UiOperationState operations, string name, Func<CancellationToken, Task> execute,
        string cancelLabel = "Cancelar", Func<bool>? canExecute = null)
    {
        _operations = operations;
        _name = name;
        _execute = execute;
        _cancelLabel = cancelLabel;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _operations.IsIdle && (_canExecute?.Invoke() ?? true);
    public void Execute(object? parameter) => _ = ExecuteAsync();
    public Task ExecuteAsync() => CanExecute(null)
        ? _operations.RunAsync(_name, _execute, _cancelLabel)
        : Task.CompletedTask;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
