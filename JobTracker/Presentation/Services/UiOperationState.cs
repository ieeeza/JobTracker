using System.IO;
using JobTracker.Modules.JobAnalysis.Application.Exceptions;
using JobTracker.Presentation.Commands;
using Microsoft.Extensions.Logging;

namespace JobTracker.Presentation.Services;

/// <summary>One operation at a time, with a shared status bar and cancellation on close.</summary>
public sealed class UiOperationState(ILogger<UiOperationState> logger) : ObservableModel, IDisposable
{
    private readonly List<AsyncUiCommand> _commands = [];
    private CancellationTokenSource? _cancellation;
    private Task _runningTask = Task.CompletedTask;
    private bool _busy;
    private string _message = "Tudo pronto. Seus dados ficam neste computador.";
    private string _error = "";
    private string _cancelLabel = "Cancelar";

    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Raise(nameof(IsIdle)); foreach (var command in _commands) command.Refresh(); } }
    public bool IsIdle => !IsBusy;
    public string Message { get => _message; set => Set(ref _message, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public string CancelLabel { get => _cancelLabel; private set => Set(ref _cancelLabel, value); }

    public AsyncUiCommand Command(string name, Func<CancellationToken, Task> execute,
        string cancelLabel = "Cancelar", Func<bool>? canExecute = null)
    {
        var command = new AsyncUiCommand(this, name, execute, cancelLabel, canExecute);
        _commands.Add(command);
        return command;
    }

    public Task RunAsync(string name, Func<CancellationToken, Task> execute, string cancelLabel = "Cancelar")
    {
        if (IsBusy) return Task.CompletedTask;
        return _runningTask = ExecuteAsync(name, execute, cancelLabel);
    }

    private async Task ExecuteAsync(string name, Func<CancellationToken, Task> execute, string cancelLabel)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        CancelLabel = cancelLabel;
        Error = "";
        IsBusy = true;
        try
        {
            Message = "Processando…";
            await execute(cancellation.Token);
            if (Message == "Processando…") Message = "Operação concluída.";
        }
        catch (OperationCanceledException)
        {
            Message = "Operação cancelada. Os dados do formulário foram preservados. Atualize a lista se a gravação já havia começado.";
            Error = Message;
        }
        catch (Exception ex)
        {
            logger.LogError("Operação {Operation} falhou: {ErrorType}. Local: {Stack}", name, ex.GetType().Name, ex.StackTrace);
            Message = ex switch
            {
                Microsoft.Data.Sqlite.SqliteException => "Não foi possível acessar o SQLite. Verifique espaço, permissões e se outro programa está usando o banco.",
                Microsoft.EntityFrameworkCore.DbUpdateException => "A gravação falhou. Atualize os dados e verifique o banco local.",
                ArgumentException or InvalidOperationException or JobPersistenceException or DuplicateJobException or TimeoutException or InvalidDataException => ex.Message,
                System.Text.Json.JsonException => "O arquivo JSON não corresponde ao formato de exportação do JobTracker. Nenhum registro foi importado.",
                IOException or UnauthorizedAccessException => "Não foi possível acessar o arquivo. Verifique o caminho, as permissões e o espaço disponível.",
                _ => "A operação falhou. Seus dados não foram descartados; consulte o log de diagnóstico na pasta local."
            };
            Error = Message;
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
            CancelLabel = "Cancelar";
        }
    }

    public void Cancel() => _cancellation?.Cancel();
    public async Task CancelAndWaitAsync() { Cancel(); await _runningTask; }
    public void Dispose() => Cancel();
}
