using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Presentation;

public sealed class ImageItemViewModel(JobImageInput input, BitmapSource thumbnail) : ObservableModel
{
    private int _order;
    private string _status = "Aguardando análise";
    public JobImageInput Input { get; } = input;
    public string Name => Input.Name;
    public BitmapSource Thumbnail { get; } = thumbnail;
    public int Order { get => _order; set => Set(ref _order, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
}

public sealed class ImageImportViewModel(IJobImageImporter importer, JobImageAnalysisService analysis,
    ImageLimits options, DesktopImageServices desktop) : ObservableModel
{
    private bool _hasAnalysis, _analyzing;
    private long _operationVersion;
    private string _reviewText = "";
    public OllamaOptions OllamaOptions { get; set; } = new();
    private ImageItemViewModel? _selected;
    private string _status = "Adicione os prints na ordem de leitura.", _warnings = "", _source = "", _reference = "";
    private double _progress;
    private long _bytes;
    private OcrCapture? _capture;
    private int _revision, _analyzedRevision;
    public ObservableCollection<ImageItemViewModel> Items { get; } = [];
    public ImageItemViewModel? Selected { get => _selected; set => Set(ref _selected, value); }
    public string CountLabel => $"{Items.Count} print(s) • {_bytes / 1024 / 1024} MB de {options.MaxBatchBytes / 1024 / 1024} MB";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Warnings { get => _warnings; private set => Set(ref _warnings, value); }
    public string Source { get => _source; set => Set(ref _source, value); }
    public string JobReference { get => _reference; set => Set(ref _reference, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public int Revision => _revision;
    public bool NeedsAnalysis => Items.Count > 0 && (!_hasAnalysis || _revision != _analyzedRevision);
    public OcrCapture? Capture => _capture is null ? null : _capture with { Source = Source, JobReference = JobReference };
    public string OriginalText => _capture is null ? "" : string.Join("\n\n", _capture.Images.Select((i, n) =>
        $"── Print {n + 1}: {i.Name} ──\n{i.Text}" + (i.Error is null ? "" : "\nAviso: " + i.Error)));
    public string ConsolidatedText => _capture?.Text ?? _reviewText;
    public bool HasCapture => _capture is not null;
    public bool HasAnalysis => _hasAnalysis;

    public async Task AddFilesAsync(IReadOnlyList<string>? paths, CancellationToken ct)
    {
        paths ??= desktop.SelectFiles();
        var errors = new List<string>();
        int added = 0;
        try
        {
            foreach (string path in paths)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var input = await importer.ImportFileAsync(path, ct);
                    if (Add(input)) added++;
                    else errors.Add(Path.GetFileName(path) + ": imagem idêntica já anexada.");
                }
                catch (Exception ex) when (IsImportFailure(ex))
                {
                    errors.Add(Path.GetFileName(path) + ": " + (ex is ArgumentException ? ex.Message : "Não foi possível carregar a imagem. Confira formato, integridade e permissão de leitura."));
                }
            }
        }
        finally
        {
            Status = $"{added} print(s) adicionado(s).";
            Warnings = string.Join("\n", errors);
        }
    }

    public async Task PasteAsync(CancellationToken ct)
    {
        try
        {
            byte[] bytes = await desktop.ReadClipboardAsync(options, ct);
            var input = await importer.ImportAsync($"Captura {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png", bytes, ct);
            Status = Add(input) ? "Print colado. Adicione os próximos ou analise as imagens." : "Esta imagem já está anexada.";
        }
        catch (Exception ex) when (IsImportFailure(ex))
        { throw new InvalidOperationException(ex is ArgumentException ? ex.Message : "Não foi possível ler o print. Capture novamente ou selecione um arquivo.", ex); }
    }

    private bool Add(JobImageInput input)
    {
        if (Items.Any(i => i.Input.Fingerprint == input.Fingerprint)) return false;
        long size = input.EstimatedMemoryBytes;
        if (_bytes + size > options.MaxBatchBytes)
            throw new ArgumentException($"O conjunto excede {options.MaxBatchBytes / 1024 / 1024} MB. Remova prints para reduzir o conjunto.");
        Items.Add(new(input, DesktopImageServices.LoadThumbnail(input.Thumbnail)));
        _bytes += size; Changed(); Selected = Items[^1];
        return true;
    }

    public void RemoveSelected()
    {
        if (Selected is not { } item) return;
        _bytes -= item.Input.EstimatedMemoryBytes;
        Items.Remove(item); Selected = Items.LastOrDefault(); Changed();
    }

    public void Clear()
    {
        Items.Clear(); _bytes = 0; Selected = null; Changed();
        Status = "Prints removidos. O resultado já analisado permanece disponível para revisão.";
    }

    public void Move(int offset)
    {
        if (Selected is not { } item) return;
        int index = Items.IndexOf(item), target = index + offset;
        if (target < 0 || target >= Items.Count) return;
        Items.Move(index, target); Changed();
    }

    private void Changed()
    {
        for (int index = 0; index < Items.Count; index++) Items[index].Order = index + 1;
        _revision++; Raise(nameof(Revision)); Raise(nameof(CountLabel)); Raise(nameof(NeedsAnalysis));
        if (Items.Count > 0) Status = "Lista alterada. Analise os prints para atualizar a revisão.";
    }

    public Task PreviewAsync(CancellationToken ct) => Selected is { } item
        ? desktop.PreviewAsync(item.Input, ct)
        : throw new InvalidOperationException("Selecione um print para ampliar.");

    public async Task<JobImageAnalysisResult> AnalyzeAsync(CancellationToken ct)
    {
        if (_analyzing) throw new InvalidOperationException("Já há uma análise de imagens em andamento.");
        _analyzing = true;
        long operation = ++_operationVersion;
        int revision = _revision;
        JobImageInput[] inputs = Items.Select(i => i.Input).ToArray();
        Progress = 0; Warnings = "";
        foreach (var item in Items) item.Status = "Aguardando análise";
        bool receiving = true;
        var elapsed = Stopwatch.StartNew();
        string phase = "Preparando análise com IA local…";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void UpdateElapsed(object? sender, EventArgs args)
        {
            if (receiving && operation == _operationVersion)
                Status = $"{phase} • {elapsed.Elapsed.TotalSeconds:N0} s decorridos";
        }
        timer.Tick += UpdateElapsed;
        timer.Start();
        var progress = new Progress<ImageAnalysisProgress>(value =>
        {
            if (!receiving || operation != _operationVersion) return;
            var item = Items.FirstOrDefault(i => i.Input.Id == value.ImageId);
            if (item is not null) item.Status = value.Message;
            Progress = 100.0 * (value.Current - (value.Status == ImageAnalysisStatus.Processing ? 1 : 0)) / value.Total;
            phase = $"Imagem {value.Current} de {value.Total} • {value.Message}";
            UpdateElapsed(null, EventArgs.Empty);
        });
        try
        {
            Status = "Analisando imagens com IA local…";
            var extraction = await analysis.AnalyzeAsync(inputs, OllamaOptions, progress, ct);
            ct.ThrowIfCancellationRequested();
            if (operation != _operationVersion || revision != _revision)
                throw new OperationCanceledException("O conjunto de imagens mudou; o resultado anterior foi descartado.");
            LoadCapture(extraction.Capture);
            _hasAnalysis = true; _reviewText = extraction.ReviewText;
            Raise(nameof(HasAnalysis)); Raise(nameof(ConsolidatedText));
            _analyzedRevision = _revision;
            foreach (var item in Items)
            {
                item.Status = "Análise concluída; revise o resultado.";
            }
            Warnings = string.Join("\n", extraction.Warnings);
            receiving = false;
            Progress = 100; Status = $"Análise concluída em {elapsed.Elapsed.TotalSeconds:N1} s. Revise todos os campos antes de salvar.";
            return extraction;
        }
        catch (OperationCanceledException)
        {
            if (operation != _operationVersion) throw;
            foreach (var item in Items)
                item.Status = "Análise cancelada; resultado não aplicado.";
            Status = "Análise cancelada. O resultado anterior, se houver, foi preservado.";
            throw;
        }
        catch (Exception ex)
        {
            if (operation != _operationVersion) throw;
            Status = "A análise não foi concluída; confira os erros e tente novamente.";
            Warnings = ex is ArgumentException or InvalidOperationException ? ex.Message : "Falha na análise das imagens.";
            foreach (var item in Items)
                item.Status = "Análise não concluída; resultado não aplicado.";
            throw;
        }
        finally
        {
            receiving = false;
            timer.Stop();
            timer.Tick -= UpdateElapsed;
            _analyzing = false;
            Raise(nameof(NeedsAnalysis));
        }
    }

    public void Reset(OcrCapture? capture = null)
    {
        _operationVersion++;
        Items.Clear(); _bytes = 0; Selected = null; _revision++; _analyzedRevision = _revision;
        _hasAnalysis = capture is not null; _reviewText = "";
        Warnings = ""; Progress = 0; Status = "Adicione os prints na ordem de leitura.";
        LoadCapture(capture); Raise(nameof(CountLabel)); Raise(nameof(Revision)); Raise(nameof(NeedsAnalysis)); Raise(nameof(HasAnalysis));
    }

    private void LoadCapture(OcrCapture? capture)
    {
        _capture = capture; Source = capture?.Source ?? ""; JobReference = capture?.JobReference ?? "";
        Raise(nameof(OriginalText)); Raise(nameof(ConsolidatedText)); Raise(nameof(HasCapture));
    }

    private static bool IsImportFailure(Exception ex) => ex is IOException or FileFormatException or ArgumentException or
        NotSupportedException or UnauthorizedAccessException or COMException;
}
