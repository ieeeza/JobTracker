using JobTracker.Modules.JobAnalysis.Domain;
namespace JobTracker.Presentation;

public sealed partial class ShellViewModel
{
    private string _importMessage = "", _analysisWarnings = "";
    private bool _focusEditorWhenIdle;
    private int _screenshotModeIndex;
    public int ScreenshotModeIndex
    {
        get => _screenshotModeIndex;
        set { if (!IsBusy && value is 0 or 1 && Set(ref _screenshotModeIndex, value)) { Raise(nameof(IsFolderImport)); Raise(nameof(IsSingleJobForm)); } }
    }
    public bool IsFolderImport => ScreenshotModeIndex == 1;
    public bool IsSingleJobForm => !IsFolderImport;
    public event EventHandler? EditorFocusRequested;
    public string ImportMessage { get => _importMessage; private set => Set(ref _importMessage, value); }
    public string AnalysisWarnings { get => _analysisWarnings; private set => Set(ref _analysisWarnings, value); }
    private void ApplyAnalysis(JobContent content, string url, IReadOnlyList<string> warnings)
    {
        var notices = warnings.ToList();
        string Merge(string label, string current, string candidate, int limit)
        {
            if (candidate.Length > limit)
            {
                notices.Add(label + ": sugestão maior que o limite do campo. Consulte o conteúdo original e preencha manualmente.");
                return current;
            }
            if (string.IsNullOrWhiteSpace(current)) return candidate;
            if (!string.IsNullOrWhiteSpace(candidate) && !current.Trim().Equals(candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                notices.Add(label + ": valor já preenchido preservado. Compare com a origem antes de editar.");
            return current;
        }
        Editor.Fill(Editor.Content with
        {
            Title = Merge("Cargo", Editor.Title, content.Title, 300),
            Company = Merge("Empresa", Editor.Company, content.Company, 200),
            Description = Merge("Descrição", Editor.Description, content.Description, 200000),
            Location = Merge("Localização", Editor.Location, content.Location, 500),
            WorkMode = Merge("Modalidade", Editor.WorkMode, content.WorkMode, 500),
            Seniority = Merge("Senioridade", Editor.Seniority, content.Seniority, 500),
            Contract = Merge("Contrato", Editor.Contract, content.Contract, 500),
            Salary = Merge("Remuneração", Editor.Salary, content.Salary, 500),
            Requirements = Merge("Requisitos", Editor.Requirements, content.Requirements, 50000),
            Technologies = Merge("Tecnologias", Editor.Technologies, content.Technologies, 8000),
            Channel = Merge("Canal", Editor.Channel, content.Channel, 500)
        });
        if (!IsExisting) Editor.Url = Merge("URL", Editor.Url, url, 8000);
        AnalysisWarnings = string.Join("\n", notices);
    }

    private void ResetImport()
    {
        Images.Reset(); ImportMessage = AnalysisWarnings = "";
        DuplicateMatches.Clear(); SelectedDuplicate = null;
        DuplicateSummary = "Confira correspondências após preencher empresa e cargo.";
    }
}
