namespace JobTracker.Modules.JobAnalysis.Domain;

public partial class Job
{
    public Job(
        string source,
        string? externalJobId,
        string originalUrl,
        string normalizedUrl,
        string? company,
        string? title,
        string? description,
        DateTime appliedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(originalUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUrl);
        ValidateLength(source, 100, nameof(source));
        ValidateLength(externalJobId, 500, nameof(externalJobId));
        Source = source;
        ExternalJobId = externalJobId;
        OriginalUrl = originalUrl;
        NormalizedUrl = normalizedUrl;
        UpdateDetails(company, title, description, appliedAt);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Source { get; private set; }
    public string? ExternalJobId { get; private set; }
    public string OriginalUrl { get; private set; }
    public string NormalizedUrl { get; private set; }
    public string? Company { get; private set; }
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public DateTime AppliedAt { get; private set; }

    public void UpdateDetails(string? company, string? title, string? description, DateTime appliedAt)
    {
        ValidateLength(company, 200, nameof(company));
        ValidateLength(title, 300, nameof(title));
        if (appliedAt == default || appliedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A data da candidatura deve estar preenchida em UTC.", nameof(appliedAt));

        Company = string.IsNullOrWhiteSpace(company) ? null : company;
        Title = string.IsNullOrWhiteSpace(title) ? null : title;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
        AppliedAt = appliedAt;
    }

    public void ChangeReference(string source, string originalUrl, string normalizedUrl, string? externalJobId)
    {
        if (originalUrl.Length > 0 && (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)) throw new ArgumentException("URL de referência inválida.");
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ValidateLength(source, 100, nameof(source));
        ValidateLength(originalUrl, 8000, nameof(originalUrl));
        ValidateLength(externalJobId, 500, nameof(externalJobId));
        if (Source == source && OriginalUrl == originalUrl && NormalizedUrl == normalizedUrl && ExternalJobId == externalJobId) return;
        Source = source; OriginalUrl = originalUrl; NormalizedUrl = normalizedUrl; ExternalJobId = externalJobId;
        Record("URL de referência atualizada");
    }

    private static void ValidateLength(string? value, int maximumLength, string parameterName)
    {
        if (value?.Length > maximumLength)
            throw new ArgumentException($"O campo excede o limite de {maximumLength} caracteres.", parameterName);
    }
}
