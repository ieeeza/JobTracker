using JobTracker.Modules.JobApplications.Domain;

namespace JobTracker.Modules.JobAnalysis.Domain;

public sealed record JobContent(string Title, string Company, string Description, string Location,
    string WorkMode, string Seniority, string Contract, string Salary, string Requirements,
    string Notes, string Channel, string NextAction, DateTime? NextActionDate, string Technologies);

public partial class Job
{
    // AppliedAt is retained for compatibility with the original schema. HasApplied distinguishes
    // saved opportunities; a saved opportunity's technical creation time is never shown as an application.
    public bool HasApplied { get; private set; }
    public ApplicationStatus Status { get; private set; } = ApplicationStatus.Saved;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public Guid Revision { get; private set; } = Guid.NewGuid();
    public bool IsDeleted { get; private set; }
    public string? CanonicalUrl { get; private set; }
    public string Location { get; private set; } = "";
    public string WorkMode { get; private set; } = "";
    public string Seniority { get; private set; } = "";
    public string Contract { get; private set; } = "";
    public string Salary { get; private set; } = "";
    public string Requirements { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public string Channel { get; private set; } = "";
    public string NextAction { get; private set; } = "";
    public DateTime? NextActionDate { get; private set; }
    public string ExtractionMetadata { get; private set; } = "Manual";
    public List<JobEvent> Events { get; private set; } = [];
    public List<Appointment> Appointments { get; private set; } = [];
    public List<JobTag> Tags { get; private set; } = [];

    public void Edit(JobContent content)
    {
        if (string.IsNullOrWhiteSpace(content.Title)) throw new ArgumentException("Informe o cargo da vaga.");
        if (string.IsNullOrWhiteSpace(content.Company)) throw new ArgumentException("Informe a empresa da vaga.");
        ValidateLength(content.Description, 200000, nameof(content.Description));
        ValidateLength(content.Requirements, 50000, nameof(content.Requirements));
        ValidateLength(content.Notes, 50000, nameof(content.Notes));
        ValidateLength(content.Technologies, 8100, nameof(content.Technologies));
        foreach (string value in new[] { content.Location, content.WorkMode, content.Seniority,
                     content.Contract, content.Salary, content.Channel, content.NextAction })
            ValidateLength(value, 500, nameof(content));
        UpdateDetails(content.Company.Trim(), content.Title.Trim(), content.Description, AppliedAt);
        Location = content.Location.Trim(); WorkMode = content.WorkMode.Trim();
        Seniority = content.Seniority.Trim(); Contract = content.Contract.Trim(); Salary = content.Salary.Trim();
        Requirements = content.Requirements; Notes = content.Notes; Channel = content.Channel;
        NextAction = content.NextAction;
        NextActionDate = content.NextActionDate is { } date ? DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified) : null;
        string[] names = content.Technologies.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length > 100) throw new ArgumentException("Use no máximo 100 tecnologias/tags.");
        JobTag[] tags = names.Select(name => new JobTag(name)).ToArray();
        Tags.RemoveAll(tag => !names.Contains(tag.Name, StringComparer.OrdinalIgnoreCase));
        Tags.AddRange(tags.Where(tag => !Tags.Any(existing => existing.Name.Equals(tag.Name, StringComparison.OrdinalIgnoreCase))));
        Record("Dados da vaga atualizados");
    }

    public void SetCapture(string? canonicalUrl, string metadata)
    {
        if (canonicalUrl is not null && (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0))
            throw new ArgumentException("URL canônica inválida.");
        ValidateLength(metadata, 5000, nameof(metadata));
        CanonicalUrl = canonicalUrl; ExtractionMetadata = metadata;
    }

    public void ChangeStatus(ApplicationStatus status, DateTime? applicationDateUtc = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentException("Status inválido.");
        if (IsDeleted) throw new InvalidOperationException("Recupere a vaga antes de alterar o status.");
        if (status is not (ApplicationStatus.Saved or ApplicationStatus.Archived) && !HasApplied)
        {
            DateTime date = applicationDateUtc ?? DateTime.UtcNow;
            if (date == default || date.Kind != DateTimeKind.Utc || date > DateTime.UtcNow.AddMinutes(1))
                throw new ArgumentException("Informe uma data de candidatura válida, não futura.");
            HasApplied = true; AppliedAt = date;
        }
        if (Status == status) return;
        ApplicationStatus previous = Status;
        Status = status;
        Record($"Status: {previous.DisplayName()} → {status.DisplayName()}");
    }

    public void SetDeleted(bool deleted)
    {
        IsDeleted = deleted;
        Record(deleted ? "Vaga movida para a lixeira" : "Vaga recuperada da lixeira");
    }
    public void AddAppointment(Appointment appointment)
    {
        Appointments.Add(appointment); Record("Compromisso adicionado");
    }
    public void CompleteAppointment(Guid id)
    {
        Appointment item = Appointments.SingleOrDefault(a => a.Id == id)
            ?? throw new InvalidOperationException("Compromisso não encontrado.");
        item.Complete(); Record("Compromisso concluído");
    }
    public void Record(string message)
    {
        UpdatedAt = DateTime.UtcNow; Revision = Guid.NewGuid();
        Events.Add(new JobEvent(message, UpdatedAt));
    }
    public void RestoreTracking(Guid id, ApplicationStatus status, bool hasApplied, DateTime createdAt,
        DateTime updatedAt, bool deleted, IReadOnlyList<JobEvent> events, IReadOnlyList<Appointment> appointments)
    {
        if (id == Guid.Empty || !Enum.IsDefined(status) || createdAt == default || createdAt.Kind != DateTimeKind.Utc || updatedAt.Kind != DateTimeKind.Utc || updatedAt < createdAt)
            throw new ArgumentException("Identidade, status ou datas do histórico inválidos.");
        if (status is not (ApplicationStatus.Saved or ApplicationStatus.Archived) && !hasApplied)
            throw new ArgumentException("Este status exige uma data de candidatura.");
        Id = id; Status = status; HasApplied = hasApplied; CreatedAt = createdAt; UpdatedAt = updatedAt; IsDeleted = deleted;
        Events = events.ToList(); Appointments = appointments.ToList(); Revision = Guid.NewGuid();
    }
}
