using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;

namespace JobTracker.Presentation;

public sealed class EditorViewModel : ObservableModel
{
    private string _url = "", _title = "", _company = "", _description = "", _location = "", _mode = "", _seniority = "",
        _contract = "", _salary = "", _requirements = "", _technologies = "", _notes = "", _channel = "", _next = "";
    private DateTime? _nextDate, _applicationDate = DateTime.Today;
    private ApplicationStatus _status = ApplicationStatus.Applied;
    public string Url { get => _url; set => Set(ref _url, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Company { get => _company; set => Set(ref _company, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string Location { get => _location; set => Set(ref _location, value); }
    public string WorkMode { get => _mode; set => Set(ref _mode, value); }
    public string Seniority { get => _seniority; set => Set(ref _seniority, value); }
    public string Contract { get => _contract; set => Set(ref _contract, value); }
    public string Salary { get => _salary; set => Set(ref _salary, value); }
    public string Requirements { get => _requirements; set => Set(ref _requirements, value); }
    public string Technologies { get => _technologies; set => Set(ref _technologies, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value); }
    public string Channel { get => _channel; set => Set(ref _channel, value); }
    public string NextAction { get => _next; set => Set(ref _next, value); }
    public DateTime? NextActionDate { get => _nextDate; set => Set(ref _nextDate, value); }
    public DateTime? ApplicationDate { get => _applicationDate; set => Set(ref _applicationDate, value); }
    public ApplicationStatus Status { get => _status; set => Set(ref _status, value); }
    public IReadOnlyList<StatusOption> Statuses => StatusCatalog.All;
    public JobContent Content => new(Title, Company, Description, Location, WorkMode, Seniority, Contract, Salary,
        Requirements, Notes, Channel, NextAction, NextActionDate, Technologies);
    public void Fill(JobContent content)
    {
        Title = content.Title; Company = content.Company; Description = content.Description; Location = content.Location;
        WorkMode = content.WorkMode; Seniority = content.Seniority; Contract = content.Contract; Salary = content.Salary;
        Requirements = content.Requirements; Technologies = content.Technologies; Notes = content.Notes;
        Channel = content.Channel; NextAction = content.NextAction; NextActionDate = content.NextActionDate;
    }
}
