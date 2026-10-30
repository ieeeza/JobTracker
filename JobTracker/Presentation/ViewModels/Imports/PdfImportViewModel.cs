using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Presentation.Services;
using Microsoft.Extensions.Logging;

namespace JobTracker.Presentation.ViewModels.Imports;

public sealed partial class PdfImportViewModel : ObservableObject, IDisposable
{
    private readonly IFilePickerService _filePicker;
    private readonly IPdfTextExtractor _extractor;
    private readonly IJobPdfAnalyzer _analyzer;
    private readonly UiOperationState _operations;
    private readonly ILogger<PdfImportViewModel> _logger;

    public PdfImportViewModel(IFilePickerService filePicker, IPdfTextExtractor extractor,
        IJobPdfAnalyzer analyzer, UiOperationState operations, ILogger<PdfImportViewModel> logger)
    {
        _filePicker = filePicker;
        _extractor = extractor;
        _analyzer = analyzer;
        _operations = operations;
        _logger = logger;
        _operations.PropertyChanged += OnOperationChanged;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractPdfCommand))]
    private string? _selectedPdfPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzePdfCommand))]
    private string _extractedText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Selecione um PDF para começar.";

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
    private PdfJobAnalysisResult? _analysisResult;

    public bool HasAnalysis => AnalysisResult is not null;
    public bool IsBusy => IsExtracting || IsAnalyzing;

    partial void OnSelectedPdfPathChanged(string? value)
    {
        ExtractedText = string.Empty;
        AnalysisResult = null;
    }

    partial void OnExtractedTextChanged(string value) => AnalysisResult = null;

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
        string text = ExtractedText;

        await _operations.RunAsync(nameof(AnalyzePdfAsync), async ct =>
        {
            IsAnalyzing = true;
            AnalysisResult = null;
            SetStatus("Analisando o PDF com o Qwen local…");

            try
            {
                PdfJobAnalysisResult result = await _analyzer.AnalyzeAsync(text, ct);
                ct.ThrowIfCancellationRequested();
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

    private bool CanSelectPdf() => _operations.IsIdle && !IsExtracting && !IsAnalyzing;
    private bool CanExtractPdf() => CanSelectPdf() && !string.IsNullOrWhiteSpace(SelectedPdfPath);
    private bool CanAnalyzePdf() => CanSelectPdf() && !string.IsNullOrWhiteSpace(ExtractedText);
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
    }

    private void LogFailure(string operation, Exception exception) =>
        _logger.LogWarning("PDF: operação {Operation} falhou. Tipo: {Type}; local: {Stack}",
            operation, exception.GetType().Name, exception.StackTrace);

    public void Dispose() => _operations.PropertyChanged -= OnOperationChanged;
}
