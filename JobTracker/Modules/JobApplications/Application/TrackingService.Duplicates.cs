using System.Text.RegularExpressions;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobApplications.Application;

public enum DuplicateKind { Confirmed, Possible }
public sealed record JobDuplicateMatch(JobInfo Job, DuplicateKind Kind, string Reason)
{
    public string Summary => $"{(Kind == DuplicateKind.Confirmed ? "Duplicidade confirmada" : "Possível duplicidade")}: " +
        $"{Job.Content.Company} • {Job.Content.Title} • {Job.Content.Location}\n{Reason}\n" +
        $"Status: {Job.StatusLabel} • candidatura: {Job.ApplicationLabel}" + (Job.IsDeleted ? " • Na lixeira" : "");
}

public sealed partial class TrackingService
{
    public async Task<IReadOnlyList<JobDuplicateMatch>> FindDuplicatesAsync(string url, JobContent content,
        OcrCapture? capture, Guid? excluding, CancellationToken ct) =>
        FindMatches(await ListAsync(ct), url, content, capture, excluding);

    private IReadOnlyList<JobDuplicateMatch> FindMatches(IReadOnlyList<JobInfo> jobs, string url,
        JobContent content, OcrCapture? capture, Guid? excluding)
    {
        string? normalized = string.IsNullOrWhiteSpace(url) ? null : identifier.Identify(url).NormalizedUrl;
        var matches = new List<JobDuplicateMatch>();
        foreach (var job in jobs.Where(j => j.Id != excluding))
        {
            if (normalized is not null && MatchesUrl(job, normalized))
            { matches.Add(new(job, DuplicateKind.Confirmed, "Mesmo endereço ou identificador da URL.")); continue; }
            bool sameCompany = !string.IsNullOrWhiteSpace(content.Company) &&
                JobTextKey.Normalize(job.Content.Company) == JobTextKey.Normalize(content.Company);
            if (!sameCompany) continue;
            bool reference = capture is { JobReference.Length: > 0 } &&
                (string.Equals(capture.JobReference, job.OcrCapture?.JobReference, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(capture.JobReference, job.ExternalJobId, StringComparison.OrdinalIgnoreCase));
            bool exact = Similar(job, content);
            bool textEvidence = Overlap(content.Title, job.Content.Title) >= .6 &&
                Overlap(content.Description, job.Content.Description, 12) >= .6;
            if (!reference && !exact && !textEvidence) continue;
            string reason = reference ? "Mesma empresa e identificador lido no print; confirme o OCR."
                : exact ? "Mesma empresa e cargo." : "Mesma empresa, cargos e descrições semelhantes.";
            if (content.Location.Length > 0 && job.Content.Location.Length > 0)
                reason += JobTextKey.Normalize(content.Location) == JobTextKey.Normalize(job.Content.Location)
                    ? " Localização igual." : " Localizações diferentes; pode ser outra oportunidade.";
            if (Overlap(content.Technologies, job.Content.Technologies) >= .5) reason += " Tecnologias em comum (indício complementar).";
            if (Overlap(content.Requirements, job.Content.Requirements, 8) >= .6) reason += " Requisitos em comum.";
            matches.Add(new(job, DuplicateKind.Possible, reason));
        }
        return matches;
    }

    private static double Overlap(string left, string right, int minimum = 1)
    {
        static HashSet<string> Tokens(string text) => Regex.Matches(JobTextKey.Normalize(text),
            @"[\p{L}\p{N}+#.]{2,}", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Value).ToHashSet();
        var first = Tokens(left); var second = Tokens(right);
        if (first.Count < minimum || second.Count < minimum) return 0;
        return first.Intersect(second).Count() / (double)Math.Max(first.Count, second.Count);
    }
}
