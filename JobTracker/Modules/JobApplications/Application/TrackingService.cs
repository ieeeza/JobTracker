using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobApplications.Domain;

namespace JobTracker.Modules.JobApplications.Application;

public sealed record StatusOption(ApplicationStatus Value, string Label);
public static class StatusCatalog
{
    public static IReadOnlyList<StatusOption> All { get; } = Enum.GetValues<ApplicationStatus>().Select(s => new StatusOption(s, s.DisplayName())).ToArray();
    public static string Label(ApplicationStatus status) => All.Single(s => s.Value == status).Label;
}
public sealed record EventInfo(DateTime OccurredAt, string Description)
{
    public string Display => $"{OccurredAt.ToLocalTime():dd/MM/yyyy HH:mm} • {Description}";
}
public sealed record AppointmentInfo(Guid Id, Guid JobId, string JobTitle, string Title,
    DateTime LocalDateTime, string Kind, string Notes, bool Completed);
public sealed record JobInfo(Guid Id, Guid Revision, string Url, string NormalizedUrl, string? CanonicalUrl,
    string Source, string? ExternalJobId, JobContent Content, ApplicationStatus Status, DateTime? AppliedAt,
    DateTime CreatedAt, DateTime UpdatedAt, bool IsDeleted, string ExtractionMetadata,
    IReadOnlyList<EventInfo> History, IReadOnlyList<AppointmentInfo> Appointments, OcrCapture? OcrCapture = null,
    string? BatchImportKey = null, string BatchImageHashes = "")
{
    public string StatusLabel => StatusCatalog.Label(Status);
    public string ApplicationLabel => AppliedAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "Não enviada";
    public string CreatedLabel => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy");
    public static JobInfo From(Job job) => new(job.Id, job.Revision, job.OriginalUrl, job.NormalizedUrl,
        job.CanonicalUrl, job.Source, job.ExternalJobId,
        new(job.Title ?? "", job.Company ?? "", job.Description ?? "", job.Location, job.WorkMode,
            job.Seniority, job.Contract, job.Salary, job.Requirements, job.Notes, job.Channel, job.NextAction,
            job.NextActionDate, string.Join(", ", job.Tags.Select(t => t.Name))), job.Status,
        job.HasApplied ? job.AppliedAt : null, job.CreatedAt, job.UpdatedAt, job.IsDeleted, job.ExtractionMetadata,
        job.Events.OrderByDescending(e => e.OccurredAt).Select(e => new EventInfo(e.OccurredAt, e.Description)).ToArray(),
        job.Appointments.Select(a => new AppointmentInfo(a.Id, job.Id, job.Title ?? "", a.Title,
            a.LocalDateTime, a.Kind, a.Notes, a.Completed)).ToArray(), job.OcrCapture, job.BatchImportKey, job.BatchImageHashes);
}

public sealed partial class TrackingService(ITrackingRepository repository, JobIdentifier identifier)
{
    public async Task<IReadOnlyList<JobInfo>> ListAsync(CancellationToken ct) =>
        (await repository.ReadAllAsync(ct)).Select(JobInfo.From).ToArray();
    public async Task<Guid> CreateAsync(string url, JobContent content, string? canonical,
        string metadata, ApplicationStatus status, DateTime? appliedUtc, bool allowSimilar, CancellationToken ct, OcrCapture? capture = null, string? importSource = null,
        string? batchImportKey = null, IReadOnlyList<string>? batchImageHashes = null)
    {
        var identity = string.IsNullOrWhiteSpace(url) ? null : identifier.Identify(url);
        string normalized = identity?.NormalizedUrl ?? $"manual:{Guid.NewGuid():N}";
        string? normalizedCanonical = string.IsNullOrWhiteSpace(canonical) ? null : identifier.Identify(canonical).NormalizedUrl;
        Job job = new(identity?.Source ?? importSource ?? (capture is null ? "Manual" : capture.Source.Length > 0 ? capture.Source : "Prints"), identity?.ExternalJobId, identity?.OriginalUrl ?? "",
            normalized, content.Company, content.Title, content.Description, DateTime.UtcNow);
        job.Edit(content); job.SetCapture(normalizedCanonical, metadata); job.SetOcrCapture(capture);
        if (batchImportKey is not null) job.SetBatchOrigin(batchImportKey, batchImageHashes ?? throw new ArgumentException("Informe os hashes das capturas do lote."));
        job.ChangeStatus(status, appliedUtc); job.Record("Vaga cadastrada");
        IReadOnlyList<JobInfo> existing = await ListAsync(ct);
        if (batchImportKey is not null)
        {
            var imported = existing.FirstOrDefault(j => j.BatchImportKey == batchImportKey);
            if (imported is not null) return imported.Id;
            if (existing.Any(j => j.BatchImageHashes.Split('\n').Intersect(batchImageHashes ?? [], StringComparer.Ordinal).Any()))
                throw new InvalidOperationException("Uma captura deste grupo já está associada a uma vaga importada. Revise o registro existente.");
        }
        if (existing.Any(j => MatchesUrl(j, normalized) || normalizedCanonical is not null && MatchesUrl(j, normalizedCanonical)))
            throw new InvalidOperationException("Esta URL já está cadastrada, inclusive na lixeira. Abra ou recupere o registro existente.");
        var similar = FindMatches(existing, url, content, capture, null).Where(m => m.Kind == DuplicateKind.Possible).ToArray();
        if (!allowSimilar && similar.Length > 0)
            throw new SimilarJobException(similar);
        await repository.AddAsync(job, ct);
        return job.Id;
    }
    public bool MatchesUrl(JobInfo job, string normalized)
    {
        if (job.NormalizedUrl == normalized || job.CanonicalUrl == normalized) return true;
        if (string.IsNullOrEmpty(job.Url) || normalized.StartsWith("manual:", StringComparison.Ordinal)) return false;
        var stored = identifier.Identify(job.Url);
        var incoming = identifier.Identify(normalized);
        return stored.NormalizedUrl == incoming.NormalizedUrl || stored.ExternalJobId is not null &&
            stored.Source == incoming.Source && stored.ExternalJobId == incoming.ExternalJobId;
    }
    public static bool Similar(JobInfo job, JobContent content) =>
        !string.IsNullOrWhiteSpace(content.Company) && !string.IsNullOrWhiteSpace(content.Title) &&
        JobTextKey.Normalize(job.Content.Company) == JobTextKey.Normalize(content.Company) &&
        JobTextKey.Normalize(job.Content.Title) == JobTextKey.Normalize(content.Title);
    public async Task EditAsync(JobInfo job, JobContent content, ApplicationStatus status, DateTime? appliedUtc,
        string? canonical, string metadata, CancellationToken ct, OcrCapture? capture = null,
        string? batchImportKey = null, IReadOnlyList<string>? batchImageHashes = null, string? url = null)
    {
        var reference = string.IsNullOrWhiteSpace(url) ? null : identifier.Identify(url);
        if (reference is not null && (await ListAsync(ct)).Any(j => j.Id != job.Id && MatchesUrl(j, reference.NormalizedUrl)))
            throw new InvalidOperationException("A URL já pertence a outra vaga cadastrada.");
        string? normalizedCanonical = string.IsNullOrWhiteSpace(canonical) ? null : identifier.Identify(canonical).NormalizedUrl;
        if (normalizedCanonical is not null && (await ListAsync(ct)).Any(j => j.Id != job.Id && MatchesUrl(j, normalizedCanonical)))
            throw new InvalidOperationException("A URL canônica já pertence a outra vaga cadastrada.");
        if (batchImportKey is not null)
        {
            if (batchImageHashes is null) throw new ArgumentException("Informe os hashes do agrupamento corrigido.");
            var all = await ListAsync(ct);
            if (all.Any(j => j.Id != job.Id && (j.BatchImportKey == batchImportKey ||
                j.BatchImageHashes.Split('\n').Intersect(batchImageHashes, StringComparer.Ordinal).Any())))
                throw new InvalidOperationException("Uma captura ainda pertence a outro registro salvo. Revise e salve primeiro o grupo de origem.");
        }
        await repository.ChangeAsync(job.Id, job.Revision, value =>
        {
            if (url is not null) value.ChangeReference(reference?.Source ?? job.Source, reference?.OriginalUrl ?? "", reference?.NormalizedUrl ?? "manual:" + job.Id.ToString("N"), reference?.ExternalJobId);
            value.Edit(content); value.ChangeStatus(status, appliedUtc); value.SetCapture(normalizedCanonical, metadata); value.SetOcrCapture(capture ?? job.OcrCapture);
            if (batchImportKey is not null && batchImageHashes is not null) value.CorrectBatchOrigin(batchImportKey, batchImageHashes);
        }, ct);
    }
    public Task StatusAsync(JobInfo job, ApplicationStatus status, DateTime? appliedUtc, CancellationToken ct) =>
        repository.ChangeAsync(job.Id, job.Revision, value => value.ChangeStatus(status, appliedUtc), ct);
    public Task DeleteAsync(JobInfo job, bool deleted, CancellationToken ct) =>
        repository.ChangeAsync(job.Id, job.Revision, value => value.SetDeleted(deleted), ct);
    public Task ScheduleAsync(JobInfo job, string title, DateTime date, string kind, string notes, CancellationToken ct) =>
        repository.ChangeAsync(job.Id, job.Revision, value => value.AddAppointment(new(title, date, kind, notes)), ct);
    public Task CompleteAsync(JobInfo job, Guid appointmentId, CancellationToken ct) =>
        repository.ChangeAsync(job.Id, job.Revision, value => value.CompleteAppointment(appointmentId), ct);
}
public sealed class SimilarJobException(IReadOnlyList<JobDuplicateMatch> matches) : Exception("Existem vagas semelhantes. Confirme se é outra oportunidade.")
{
    public IReadOnlyList<JobDuplicateMatch> Matches { get; } = matches;
}
