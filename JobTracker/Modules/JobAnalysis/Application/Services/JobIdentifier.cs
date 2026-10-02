using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Application.Services;

public class JobIdentifier
{
    public JobIdentity Identify(string url)
    {
        var uri = new Uri(url);

        var source = IdentifySource(uri);

        var externalJobId = source switch
        {
            "Indeed" => GetIndeedJobId(uri),

            "LinkedIn" => GetLinkedInJobId(uri),

            _ => null
        };

        var normalizedUrl = NormalizeUrl(uri, source, externalJobId);

        return new JobIdentity(
            Source: source,
            ExternalJobId: externalJobId,
            OriginalUrl: url,
            NormalizedUrl: normalizedUrl
        );
    }

    private static string IdentifySource(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();

        if (host.Contains("indeed.com"))
            return "Indeed";

        if (host.Contains("linkedin.com"))
            return "LinkedIn";

        if (host.Contains("gupy.io"))
            return "Gupy";

        return uri.Host;
    }

    private static string? GetIndeedJobId(Uri uri)
    {
        var query = ParseQuery(uri.Query);

        return query.TryGetValue("jk", out var jobId)
            ? jobId
            : null;
    }

    private static string? GetLinkedInJobId(Uri uri)
    {
        var query = ParseQuery(uri.Query);

        return query.TryGetValue("currentJobId", out var jobId)
            ? jobId
            : null;
    }

    private static string NormalizeUrl(
        Uri uri,
        string source,
        string? externalJobId)
    {
        if (source == "Indeed" &&
            !string.IsNullOrWhiteSpace(externalJobId))
        {
            return $"https://{uri.Host}/viewjob?jk={externalJobId}";
        }

        if (source == "LinkedIn" &&
            !string.IsNullOrWhiteSpace(externalJobId))
        {
            return $"https://{uri.Host}/jobs/view/{externalJobId}";
        }

        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
            .TrimEnd('/');
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .ToDictionary(
                x => Uri.UnescapeDataString(x[0]),
                x => Uri.UnescapeDataString(x[1]),
                StringComparer.OrdinalIgnoreCase
            );
    }
}