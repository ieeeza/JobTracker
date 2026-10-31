using System.Globalization;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;

using JobTracker.Presentation.Commands;
using JobTracker.Presentation.Services;

namespace JobTracker.Presentation.ViewModels;

public sealed class OllamaSettingsViewModel : ObservableModel
{
    private readonly ImageAnalysisSettings settings;
    private readonly IOllamaConnection connection;
    public OllamaSettingsViewModel(ImageAnalysisSettings settings, IOllamaConnection connection, UiOperationState operations)
    {
        this.settings = settings;
        this.connection = connection;
        VerifyCommand = operations.Command(nameof(VerifyCommand), ct => VerifyAsync(false, ct));
        VerifySavedCommand = operations.Command(nameof(VerifySavedCommand), ct => VerifyAsync(true, ct));
        VerifyPdfCommand = operations.Command(nameof(VerifyPdfCommand), ct => VerifyModelAsync(ReadOptions(), true, ct));
        SaveCommand = operations.Command(nameof(SaveCommand), async ct =>
        {
            await SaveAsync(ct);
            operations.Message = "Configuração Ollama salva neste computador.";
        });
    }
    public AsyncUiCommand VerifyCommand { get; }
    public AsyncUiCommand VerifySavedCommand { get; }
    public AsyncUiCommand VerifyPdfCommand { get; }
    public AsyncUiCommand SaveCommand { get; }

    private string _baseUrl = "http://127.0.0.1:11434", _model = OllamaOptions.DefaultModel, _timeout = "300", _batchSize = "3", _maxImages = "30";
    private string _batchMegabytes = "24", _batchMegapixels = "24", _connectionStatus = "Conexão ainda não verificada.";
    private OllamaOptions _configuration = new();
    private string _contextTokens = new OllamaOptions().ContextTokens.ToString(CultureInfo.InvariantCulture);
    private string _maxContextTokens = new OllamaOptions().MaxContextTokens.ToString(CultureInfo.InvariantCulture);
    private string _pdfModel = "";
    private string _pdfContextTokens = new OllamaOptions().PdfContextTokens.ToString(CultureInfo.InvariantCulture);
    private string _pdfMaxContextTokens = new OllamaOptions().PdfMaxContextTokens.ToString(CultureInfo.InvariantCulture);
    private string _pdfKeepAliveSeconds = new OllamaOptions().PdfKeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
    private bool _waitForCompletion = true;
    private bool _preferLowMemoryIndividualAnalysis = true;
    public bool PreferLowMemoryIndividualAnalysis { get => _preferLowMemoryIndividualAnalysis; set => Set(ref _preferLowMemoryIndividualAnalysis, value); }
    public OllamaOptions Configuration { get => _configuration; private set => Set(ref _configuration, value); }
    public string BaseUrl { get => _baseUrl; set { if (Set(ref _baseUrl, value)) InvalidateConnection(); } }
    public string Model { get => _model; set { if (Set(ref _model, value)) InvalidateConnection(); } }
    public string PdfModel { get => _pdfModel; set { if (Set(ref _pdfModel, value)) InvalidateConnection(); } }
    public string PdfContextTokens { get => _pdfContextTokens; set => Set(ref _pdfContextTokens, value); }
    public string PdfMaxContextTokens { get => _pdfMaxContextTokens; set => Set(ref _pdfMaxContextTokens, value); }
    public string PdfKeepAliveSeconds { get => _pdfKeepAliveSeconds; set => Set(ref _pdfKeepAliveSeconds, value); }
    private string _keepAlive = "600";
    public string KeepAliveSeconds { get => _keepAlive; set => Set(ref _keepAlive, value); }
    public string TimeoutSeconds { get => _timeout; set => Set(ref _timeout, value); }
    public string ContextTokens { get => _contextTokens; set => Set(ref _contextTokens, value); }
    public string MaxContextTokens { get => _maxContextTokens; set => Set(ref _maxContextTokens, value); }
    public bool WaitForCompletion { get => _waitForCompletion; set => Set(ref _waitForCompletion, value); }
    public string BatchSize { get => _batchSize; set => Set(ref _batchSize, value); }
    public string MaxImages { get => _maxImages; set => Set(ref _maxImages, value); }
    public string BatchMegabytes { get => _batchMegabytes; set => Set(ref _batchMegabytes, value); }
    public string BatchMegapixels { get => _batchMegapixels; set => Set(ref _batchMegapixels, value); }
    public string ConnectionStatus { get => _connectionStatus; private set => Set(ref _connectionStatus, value); }
    public void InvalidateConnection() => ConnectionStatus = "Conexão ainda não verificada para esta configuração.";
    public void ReportAnalysisFailure(ImageAnalysisException exception) => ConnectionStatus = exception.Message;
    public void ReportAnalysisSuccess() => ConnectionStatus = $"Análise concluída com {Configuration.Model} no Ollama local.";

    public async Task LoadAsync(CancellationToken ct)
    {
        var loaded = await settings.LoadAsync(ct);
        Configuration = loaded;
        BaseUrl = Configuration.BaseUrl; Model = Configuration.Model;
        PdfModel = Configuration.PdfModel;
        PdfContextTokens = Configuration.PdfContextTokens.ToString(CultureInfo.InvariantCulture);
        PdfMaxContextTokens = Configuration.PdfMaxContextTokens.ToString(CultureInfo.InvariantCulture);
        PdfKeepAliveSeconds = Configuration.PdfKeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
        KeepAliveSeconds = Configuration.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
        TimeoutSeconds = Configuration.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        ContextTokens = Configuration.ContextTokens.ToString(CultureInfo.InvariantCulture);
        MaxContextTokens = Configuration.MaxContextTokens.ToString(CultureInfo.InvariantCulture);
        WaitForCompletion = Configuration.WaitForCompletion;
        PreferLowMemoryIndividualAnalysis = Configuration.PreferLowMemoryIndividualAnalysis;
        BatchSize = Configuration.BatchSize.ToString(CultureInfo.InvariantCulture);
        MaxImages = Configuration.MaxImages.ToString(CultureInfo.InvariantCulture);
        BatchMegabytes = (Configuration.MaxBatchImageBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);
        BatchMegapixels = (Configuration.MaxBatchPixels / 1000000m).ToString(CultureInfo.InvariantCulture);
        InvalidateConnection();
    }

    public async Task SaveAsync(CancellationToken ct)
    {
        var options = ReadOptions();
        await settings.SaveOptionsAsync(options, ct);
        Configuration = options;
        InvalidateConnection();
    }

    public Task VerifyAsync(bool useSavedConfiguration, CancellationToken ct) =>
        VerifyModelAsync(useSavedConfiguration ? Configuration : ReadOptions(), false, ct);

    private async Task VerifyModelAsync(OllamaOptions options, bool pdf, CancellationToken ct)
    {
        ConnectionStatus = "Verificando Ollama e modelo local…";
        try
        {
            if (pdf) await connection.VerifyPdfAsync(options, ct);
            else await connection.VerifyAsync(options, ct);
            ConnectionStatus = (options != Configuration ? "Configuração em edição (ainda não salva): " : "") +
                $"Ollama disponível • {(pdf ? options.PdfModelName() : options.Model)} compatível com {(pdf ? "PDFs" : "imagens")} • {DateTime.Now:HH:mm:ss}. A inferência ainda pode falhar.";
        }
        catch (ImageAnalysisException ex) { ConnectionStatus = ex.Message; throw; }
        catch (OperationCanceledException) { ConnectionStatus = "Verificação cancelada. Disponibilidade não confirmada."; throw; }
    }

    private OllamaOptions ReadOptions()
    {
        if (!int.TryParse(KeepAliveSeconds, out int keepAlive) || !int.TryParse(TimeoutSeconds, out int timeout) || !int.TryParse(ContextTokens, out int contextTokens) ||
            !int.TryParse(MaxContextTokens, out int maxContextTokens) || !int.TryParse(BatchSize, out int batch) ||
            !int.TryParse(PdfContextTokens, out int pdfContext) || !int.TryParse(PdfMaxContextTokens, out int pdfMaxContext) ||
            !int.TryParse(PdfKeepAliveSeconds, out int pdfKeepAlive) ||
            !int.TryParse(MaxImages, out int maximum) || !int.TryParse(BatchMegabytes, out int megabytes) ||
            !decimal.TryParse(BatchMegapixels, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal megapixels) ||
            megabytes is < 1 or > 64 || megapixels is < 0.01m or > 64)
            throw new ArgumentException("Preencha os limites Ollama com números válidos. Para pixels, use ponto decimal (ex.: 24 ou 0.5).");
        var options = new OllamaOptions
        {
            BaseUrl = BaseUrl.Trim(),
            Model = Model.Trim(),
            PdfModel = PdfModel.Trim(),
            PdfContextTokens = pdfContext,
            PdfMaxContextTokens = pdfMaxContext,
            PdfKeepAliveSeconds = pdfKeepAlive,
            KeepAliveSeconds = keepAlive,
            MaxPreparedDimension = Configuration.MaxPreparedDimension,
            TimeoutSeconds = timeout,
            BatchSize = batch,
            MaxImages = maximum,
            ContextTokens = contextTokens,
            MaxContextTokens = maxContextTokens,
            WaitForCompletion = WaitForCompletion,
            PreferLowMemoryIndividualAnalysis = PreferLowMemoryIndividualAnalysis,
            MaxBatchImageBytes = megabytes * 1024L * 1024,
            MaxBatchPixels = (long)(megapixels * 1000000)
        };
        options.Validate();
        return options;
    }
}
