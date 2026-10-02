namespace JobTracker.Modules.JobAnalysis.Domain
{
    public class Job(
        string source,
        string? externalJobId,
        string OriginalUrl,
        string NormalizedUrl,
        string? company,
        string? title,
        string? description,
        DateTime appliedAt)
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public string Source { get; private set; } = source;
        public string? ExternalJobId { get; private set; } = externalJobId;
        public string OriginalUrl { get; private set; } = OriginalUrl;
        public string NormalizedUrl { get; private set; } = NormalizedUrl;
        public string? Company { get; private set; } = company;
        public string? Title { get; private set; } = title;
        public string? Description { get; private set; } = description;
        public DateTime AppliedAt { get; private set; } = appliedAt;
    }
}
