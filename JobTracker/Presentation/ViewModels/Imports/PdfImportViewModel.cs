using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Modules.JobAnalysis.Application.Exceptions;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Presentation.Services;
using Microsoft.Extensions.Logging;

namespace JobTracker.Presentation.ViewModels.Imports;

public sealed partial class PdfImportViewModel : ObservableObject, IDisposable
{
    private const string PdfImportMetadata = "Importação de PDF com IA local; dados revisados pelo usuário.";
    private readonly IFilePickerService _filePicker;
    private readonly IPdfTextExtractor _extractor;
    private readonly IJobPdfAnalyzer _analyzer;
    private readonly UiOperationState _operations;
    private readonly ILogger<PdfImportViewModel> _logger;
    private readonly TrackingService _tracking;
    private readonly JobWorkspace _workspace;
    private readonly DesktopServices _desktop;
    private readonly DispatcherTimer _estimateDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private CancellationTokenSource? _estimateCancellation;
    private bool _disposed;

    public PdfImportViewModel(IFilePickerService filePicker, IPdfTextExtractor extractor,
        IJobPdfAnalyzer analyzer, UiOperationState operations, ILogger<PdfImportViewModel> logger,
        TrackingService tracking, JobWorkspace workspace, DesktopServices desktop)
    {
        _filePicker = filePicker;
        _extractor = extractor;
        _analyzer = analyzer;
        _operations = operations;
        _logger = logger;
        _tracking = tracking;
        _workspace = workspace;
        _desktop = desktop;
        _estimateDelay.Tick += OnEstimateTick;
        _operations.PropertyChanged += OnOperationChanged;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractPdfCommand))]
    private string? _selectedPdfPath;

    [ObservableProperty]
    private string _extractedText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzePdfCommand), nameof(RestoreOriginalTextCommand), nameof(CleanTextCommand))]
    private string _editedText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCleanupCommand))]
    private string? _textBeforeCleanup;

    public bool CanEditText => _operations.IsIdle && !IsBusy;

    [ObservableProperty]
    private string _statusMessage = "Selecione um PDF para começar.";

    [ObservableProperty]
    private string _contextEstimate = "Extraia ou insira o texto para estimar o contexto.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(SelectPdfCommand), nameof(ExtractPdfCommand), nameof(AnalyzePdfCommand), nameof(CancelPdfCommand))]
    private bool _isExtracting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(SelectPdfCommand), nameof(ExtractPdfCommand), nameof(AnalyzePdfCommand), nameof(CancelPdfCommand))]
    private bool _isAnalyzing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnalysis))]
    [NotifyPropertyChangedFor(nameof(CanEditDraft))]
    [NotifyCanExecuteChangedFor(nameof(SavePdfCommand))]
    private PdfJobAnalysisResult? _analysisResult;

    [ObservableProperty]
    private EditorViewModel _draft = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(CanEditDraft))]
    [NotifyCanExecuteChangedFor(nameof(SavePdfCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditDraft))]
    [NotifyCanExecuteChangedFor(nameof(SavePdfCommand))]
    private bool _isSaved;

    public bool HasAnalysis => AnalysisResult is not null;
    public bool IsBusy => IsExtracting || IsAnalyzing || IsSaving;
    public bool CanEditDraft => HasAnalysis && !IsSaved && _operations.IsIdle;

    partial void OnAnalysisResultChanged(PdfJobAnalysisResult? value)
    {
        IsSaved = false;
        var draft = new EditorViewModel { Status = ApplicationStatus.Saved, ApplicationDate = null };
        if (value is not null)
        {
            draft.Fill(value.Content);
            draft.Url = value.Url;
        }
        Draft = draft;
    }

    partial void OnSelectedPdfPathChanged(string? value)
    {
        IsSaved = false;
        ExtractedText = string.Empty;
        EditedText = string.Empty;
        AnalysisResult = null;
        TextBeforeCleanup = null;
    }

    partial void OnExtractedTextChanged(string value) => EditedText = value;

    partial void OnEditedTextChanged(string value)
    {
        AnalysisResult = null;
        TextBeforeCleanup = null;
        QueueEstimate();
        SetStatus(string.IsNullOrWhiteSpace(value)
            ? "O editor está vazio. Restaure o original ou insira o texto de uma vaga."
            : "Texto atualizado. Confira o conteúdo antes de analisar com IA.");
    }

    private void QueueEstimate()
    {
        _estimateDelay.Stop();
        _estimateCancellation?.Cancel();
        if (_disposed || !CanEditText) return;
        ContextEstimate = string.IsNullOrWhiteSpace(EditedText)
            ? "Insira texto para estimar o contexto."
            : $"{EditedText.Length:N0} caracteres • atualizando estimativa…";
        if (!string.IsNullOrWhiteSpace(EditedText)) _estimateDelay.Start();
    }

    private async void OnEstimateTick(object? sender, EventArgs e)
    {
        _estimateDelay.Stop();
        if (_disposed || !CanEditText) return;
        string text = EditedText;
        using var cancellation = new CancellationTokenSource();
        _estimateCancellation = cancellation;
        try
        {
            var estimate = await _analyzer.EstimateAsync(text, cancellation.Token);
            if (cancellation.IsCancellationRequested || _disposed || EditedText != text) return;
            ContextEstimate = $"{estimate.Characters:N0} caracteres • {estimate.Utf8Bytes:N0} bytes UTF-8\n" +
                $"Texto ≈ {estimate.TextTokens:N0} tokens; leitura e resposta completas ≈ {estimate.RequiredTokens:N0} tokens.\n" +
                (estimate.BlockCount == 1
                    ? $"Contexto automático previsto: {estimate.ContextTokens:N0} tokens."
                    : $"Previsão: {estimate.BlockCount} blocos, com até {estimate.ContextTokens:N0} tokens por bloco.") +
                $" Limite de segurança: {estimate.MaximumContextTokens:N0}.\n" +
                "Valores aproximados. O limite do modelo será conferido ao analisar.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer edit superseded this estimate; its message must remain untouched.
            return;
        }
        catch (Exception ex)
        {
            if (cancellation.IsCancellationRequested || _disposed || EditedText != text) return;
            LogFailure(nameof(OnEstimateTick), ex);
            ContextEstimate = ex is ArgumentException or ImageAnalysisException
                ? ex.Message
                : "Não foi possível estimar o contexto. Confira as configurações do Ollama.";
        }
        finally
        {
            if (ReferenceEquals(_estimateCancellation, cancellation)) _estimateCancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreOriginalText))]
    private void RestoreOriginalText()
    {
        if (!CanRestoreOriginalText()) return;
        EditedText = ExtractedText;
        SetStatus("Texto original restaurado. As edições eram apenas em memória; o PDF não foi alterado.");
    }

    [RelayCommand(CanExecute = nameof(CanCleanText))]
    private void CleanText()
    {
        if (!CanCleanText()) return;
        string cleaned = PdfTextCleanup.Clean(EditedText);
        string beforeCleanup = EditedText;
        bool changed = cleaned != EditedText;
        EditedText = cleaned;
        if (changed) TextBeforeCleanup = beforeCleanup;
        SetStatus(changed
            ? "Limpeza aplicada. Revise o texto; Desfazer limpeza recupera suas edições anteriores."
            : "Nenhum trecho identificado com segurança para remoção. O texto foi mantido.");
    }

    [RelayCommand(CanExecute = nameof(CanUndoCleanup))]
    private void UndoCleanup()
    {
        if (!CanUndoCleanup() || TextBeforeCleanup is not { } previous) return;
        EditedText = previous;
        SetStatus("Limpeza desfeita. As edições anteriores foram recuperadas.");
    }

    [RelayCommand(CanExecute = nameof(CanSelectPdf))]
    private void SelectPdf()
    {
        if (!CanSelectPdf()) return;

        try
        {
            string? filepath = _filePicker.selectPdf();
            if (filepath is null) return;

            SelectedPdfPath = filepath;
            // Selecting the same path again may point to a file changed on disk.
            ExtractedText = string.Empty;
            EditedText = string.Empty;
            AnalysisResult = null;
            SetStatus("PDF selecionado. Extraia o texto para continuar.");
        }
        catch (Exception ex)
        {
            LogFailure(nameof(SelectPdf), ex);
            SetStatus("Não foi possível selecionar o PDF. Verifique o acesso ao arquivo e tente novamente.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanExtractPdf))]
    private async Task ExtractPdfAsync()
    {
        if (!CanExtractPdf() || SelectedPdfPath is not { } filepath) return;

        await _operations.RunAsync(nameof(ExtractPdfAsync), async ct =>
        {
            IsExtracting = true;
            ExtractedText = string.Empty;
            EditedText = string.Empty;
            AnalysisResult = null;
            SetStatus("Extraindo texto do PDF…");

            try
            {
                string text = await Task.Run(() => _extractor.Extract(filepath, ct), ct);
                ct.ThrowIfCancellationRequested();
                ExtractedText = text;
                SetStatus(string.IsNullOrWhiteSpace(text)
                    ? "O PDF não contém texto extraível. Documentos digitalizados precisam de OCR."
                    : "Texto extraído. Clique em Analisar com IA para identificar a vaga.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetStatus("Extração cancelada.");
            }
            catch (Exception ex)
            {
                LogFailure(nameof(ExtractPdfAsync), ex);
                SetStatus("Não foi possível ler o PDF. Confira se o arquivo existe, pode ser acessado e não está corrompido ou protegido por senha.");
            }
            finally
            {
                IsExtracting = false;
            }
        }, "Cancelar extração");
    }

    [RelayCommand(CanExecute = nameof(CanAnalyzePdf))]
    private async Task AnalyzePdfAsync()
    {
        if (!CanAnalyzePdf()) return;
        string text = EditedText;

        await _operations.RunAsync(nameof(AnalyzePdfAsync), async ct =>
        {
            IsAnalyzing = true;
            AnalysisResult = null;
            SetStatus("Analisando o PDF com o Qwen local…");

            try
            {
                var progress = new Progress<PdfAnalysisProgress>(value =>
                {
                    if (!IsAnalyzing || ct.IsCancellationRequested || EditedText != text) return;
                    SetStatus($"Analisando bloco {value.Block} de {value.BlockCount} • contexto de {value.ContextTokens:N0} tokens…");
                });
                PdfJobAnalysisResult result = await _analyzer.AnalyzeAsync(text, ct, progress);
                ct.ThrowIfCancellationRequested();
                if (EditedText != text)
                {
                    SetStatus("O texto mudou durante a análise. Analise novamente para obter dados correspondentes.");
                    return;
                }
                AnalysisResult = result;
                SetStatus("Análise concluída. Confira os dados identificados. Nenhuma vaga foi salva.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetStatus("Análise cancelada. O texto extraído foi preservado.");
            }
            catch (Exception ex)
            {
                LogFailure(nameof(AnalyzePdfAsync), ex);
                SetStatus(ex switch
                {
                    ImageAnalysisException failure => failure.Message,
                    ArgumentException => "Não foi possível analisar o PDF: " + ex.Message,
                    InvalidOperationException => "Não foi possível iniciar a análise. Confira as preferências do Ollama e tente novamente.",
                    _ => "A análise do PDF falhou. O texto foi preservado; consulte o log de diagnóstico e tente novamente."
                });
            }
            finally
            {
                IsAnalyzing = false;
            }
        }, "Cancelar análise de PDF");
    }

    [RelayCommand(CanExecute = nameof(CanCancelPdf))]
    private void CancelPdf() => _operations.Cancel();

    [RelayCommand(CanExecute = nameof(CanSavePdf))]
    private async Task SavePdfAsync()
    {
        if (!CanSavePdf()) return;
        // Keep the reviewed values stable while duplicate confirmation or persistence is pending.
        var content = Draft.Content;
        string url = Draft.Url;

        await _operations.RunAsync(nameof(SavePdfAsync), async ct =>
        {
            IsSaving = true;
            SetStatus("Validando e salvando a vaga do PDF…");
            try
            {
                try
                {
                    await _tracking.CreateAsync(url, content, null, PdfImportMetadata,
                        ApplicationStatus.Saved, null, false, ct, importSource: "PDF (IA local)");
                }
                catch (SimilarJobException ex)
                {
                    if (!_desktop.Confirm("Possível duplicidade", string.Join("\n\n", ex.Matches.Select(match => match.Summary)) +
                        "\n\nConfirma que esta é uma oportunidade diferente?"))
                    {
                        SetStatus("Cadastro cancelado. Revise os dados preservados na prévia do PDF.");
                        return;
                    }

                    await _tracking.CreateAsync(url, content, null, PdfImportMetadata,
                        ApplicationStatus.Saved, null, true, ct, importSource: "PDF (IA local)");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetStatus("Salvamento cancelado. Os dados do PDF foram preservados.");
                return;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or DuplicateJobException or JobPersistenceException)
            {
                LogFailure(nameof(SavePdfAsync), ex);
                SetStatus("Não foi possível salvar a vaga: " + ex.Message);
                return;
            }
            catch (Exception ex)
            {
                LogFailure(nameof(SavePdfAsync), ex);
                SetStatus("Falha ao salvar a vaga. Os dados foram preservados; consulte o log de diagnóstico e tente novamente.");
                return;
            }
            finally
            {
                IsSaving = false;
            }

            // Once persistence succeeds, a refresh failure must not allow a second insert.
            ClearSavedPdf();
            IsSaved = true;
            SetStatus("Vaga do PDF salva com sucesso. Campos limpos; selecione outro PDF para cadastrar a próxima vaga.");
            try
            {
                await _workspace.ReloadAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogFailure(nameof(JobWorkspace.ReloadAsync), ex);
                SetStatus("Vaga salva no banco. Não foi possível atualizar os painéis; use Atualizar para visualizar o registro.");
            }
        });
    }

    private void ClearSavedPdf()
    {
        _estimateDelay.Stop();
        _estimateCancellation?.Cancel();
        SelectedPdfPath = null;
        ExtractedText = string.Empty;
        EditedText = string.Empty;
        AnalysisResult = null;
        TextBeforeCleanup = null;
        ContextEstimate = "Insira texto para estimar o contexto.";
    }

    private bool CanSavePdf() => CanSelectPdf() && HasAnalysis && !IsSaved;
    private bool CanSelectPdf() => _operations.IsIdle && !IsBusy;
    private bool CanExtractPdf() => CanSelectPdf() && !string.IsNullOrWhiteSpace(SelectedPdfPath);
    private bool CanAnalyzePdf() => CanSelectPdf() && !string.IsNullOrWhiteSpace(EditedText);
    private bool CanRestoreOriginalText() => CanEditText && EditedText != ExtractedText;
    private bool CanCleanText() => CanEditText && !string.IsNullOrWhiteSpace(EditedText);
    private bool CanUndoCleanup() => CanEditText && TextBeforeCleanup is not null;
    private bool CanCancelPdf() => IsExtracting || IsAnalyzing;

    private void SetStatus(string message)
    {
        StatusMessage = message;
        _operations.Message = message;
    }

    private void OnOperationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UiOperationState.IsBusy)) return;
        SelectPdfCommand.NotifyCanExecuteChanged();
        ExtractPdfCommand.NotifyCanExecuteChanged();
        AnalyzePdfCommand.NotifyCanExecuteChanged();
        SavePdfCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditDraft));
        OnPropertyChanged(nameof(CanEditText));
        RestoreOriginalTextCommand.NotifyCanExecuteChanged();
        CleanTextCommand.NotifyCanExecuteChanged();
        UndoCleanupCommand.NotifyCanExecuteChanged();
        QueueEstimate();
    }

    private void LogFailure(string operation, Exception exception) =>
        _logger.LogWarning("PDF: operação {Operation} falhou. Tipo: {Type}; local: {Stack}",
            operation, exception.GetType().Name, exception.StackTrace);

    public void Dispose()
    {
        _disposed = true;
        _estimateDelay.Stop();
        _estimateDelay.Tick -= OnEstimateTick;
        _estimateCancellation?.Cancel();
        _operations.PropertyChanged -= OnOperationChanged;
    }
}
