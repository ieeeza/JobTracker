namespace JobTracker.Modules.JobApplications.Domain;

public enum ApplicationStatus
{
    Saved, Applied, Screening, Interview, TechnicalTest, Offer, Rejected, Withdrawn, Archived
}
public static class ApplicationStatusNames
{
    public static string DisplayName(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Saved => "Salva", ApplicationStatus.Applied => "Candidatura enviada",
        ApplicationStatus.Screening => "Triagem", ApplicationStatus.Interview => "Entrevista",
        ApplicationStatus.TechnicalTest => "Teste técnico", ApplicationStatus.Offer => "Proposta",
        ApplicationStatus.Rejected => "Recusada", ApplicationStatus.Withdrawn => "Desistência",
        ApplicationStatus.Archived => "Arquivada", _ => throw new ArgumentException("Status inválido.")
    };
}

public sealed class JobEvent
{
    private JobEvent() { }
    public JobEvent(string description, DateTime occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (occurredAt.Kind != DateTimeKind.Utc || occurredAt == default)
            throw new ArgumentException("O evento deve ter uma data UTC válida.");
        Description = description;
        OccurredAt = occurredAt;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid JobId { get; private set; }
    public string Description { get; private set; } = "";
    public DateTime OccurredAt { get; private set; }
}

public sealed class Appointment
{
    private Appointment() { }
    public Appointment(string title, DateTime localDateTime, string kind, string notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Length > 200 || notes.Length > 10000 || kind.Length > 100 || localDateTime == default)
            throw new ArgumentException("Revise o título, a data e as observações do compromisso.");
        Title = title.Trim();
        LocalDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        Kind = kind;
        Notes = notes;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid JobId { get; private set; }
    public string Title { get; private set; } = "";
    public DateTime LocalDateTime { get; private set; }
    public string Kind { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public bool Completed { get; private set; }
    public void Complete() => Completed = true;
}

public sealed class JobTag
{
    private JobTag() { }
    public JobTag(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 80) throw new ArgumentException("Cada tecnologia/tag pode ter até 80 caracteres.");
        Name = name.Trim();
    }
    public Guid JobId { get; private set; }
    public string Name { get; private set; } = "";
}
