using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace JobTracker.Presentation;

public sealed class DiagnosticLoggerProvider : ILoggerProvider
{
    private readonly Channel<string> _lines = Channel.CreateBounded<string>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _writer;
    public DiagnosticLoggerProvider(string directory)
    {
        _writer = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, $"diagnostico-{DateTime.Now:yyyyMMdd}.log");
                await using var writer = new StreamWriter(file, append: true) { AutoFlush = true };
                await foreach (string line in _lines.Reader.ReadAllAsync()) await writer.WriteLineAsync(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Trace.TraceError("Falha ao gravar diagnóstico: {0}", ex.GetType().Name); }
        });
    }
    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(_lines.Writer, categoryName);
    public void Dispose() => _lines.Writer.TryComplete();
    public async Task FlushAsync() { _lines.Writer.TryComplete(); await _writer; }
    private sealed class DiagnosticLogger(ChannelWriter<string> writer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) writer.TryWrite($"{DateTime.UtcNow:O} [{level}] {category}: {formatter(state, exception)}");
        }
    }
}
