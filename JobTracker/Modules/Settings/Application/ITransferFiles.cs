namespace JobTracker.Modules.Settings.Application;

public interface ITransferFiles
{
    Task<string> ReadAsync(string path, CancellationToken ct);
    Task WriteAsync(string path, string content, CancellationToken ct);
}
