using System.IO;
using System.Text;
using JobTracker.Modules.Settings.Application;

namespace JobTracker.Modules.Settings.Infrastructure;

public sealed class TransferFiles : ITransferFiles
{
    public async Task<string> ReadAsync(string path, CancellationToken ct)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (input.Length > 50 * 1024 * 1024) throw new InvalidDataException("O arquivo excede 50 MB.");
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), true);
        return await reader.ReadToEndAsync(ct);
    }
    public async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(true), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
