namespace JobTracker.Modules.JobAnalysis.Application.Models
{
    public record JobIdentity(
        string Source,
        string? ExternalJobId,
        string OriginalUrl,
        string NormalizedUrl
    );
}
