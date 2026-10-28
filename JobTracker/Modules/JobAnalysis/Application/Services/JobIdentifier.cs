using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Application.Services;

public class JobIdentifier
{
    public JobIdentity Identify(string url)
    {
        if (url?.Length > 8000) throw new ArgumentException("A URL excede 8.000 caracteres.", nameof(url));
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Insira uma URL HTTP ou HTTPS válida, sem credenciais.", nameof(url));

        string source = IdentifySource(uri);
        string? externalJobId = source switch
        {
            "Indeed" when uri.AbsolutePath is "/viewjob" or "/rc/clk" => GetSingleQueryValue(uri, "jk"),
            "LinkedIn" => GetLinkedInJobId(uri),
            _ => null
        };

        string normalizedUrl = (source, externalJobId) switch
        {
            ("Indeed", not null) => $"https://{uri.IdnHost}/viewjob?jk={Uri.EscapeDataString(externalJobId)}",
            ("LinkedIn", not null) => $"https://{uri.IdnHost}/jobs/view/{externalJobId}",
            _ => Normalize(uri)
        };

        return new JobIdentity(source, externalJobId, uri.AbsoluteUri, normalizedUrl);
    }

    private static string Normalize(Uri uri)
    {
        string[] tracking = ["fbclid", "gclid", "msclkid", "dclid", "mc_cid", "mc_eid", "trk", "trackingId", "gh_src", "lever-source", "lever-origin"];
        var builder = new UriBuilder(uri) { Fragment = "" };
        builder.Query = string.Join("&", uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                string key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
                return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !tracking.Contains(key, StringComparer.OrdinalIgnoreCase);
            }));
        return builder.Uri.AbsoluteUri;
    }

    private static string IdentifySource(Uri uri)
    {
        if (!uri.IsDefaultPort)
            return uri.IdnHost;

        string host = uri.IdnHost;
        if (IsDomain(host, "indeed.com"))
            return "Indeed";
        if (IsDomain(host, "linkedin.com"))
            return "LinkedIn";
        if (IsDomain(host, "gupy.io"))
            return "Gupy";
        if (IsDomain(host, "greenhouse.io")) return "Greenhouse";
        if (IsDomain(host, "lever.co")) return "Lever";
        return host;
    }

    private static bool IsDomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private static string? GetLinkedInJobId(Uri uri)
    {
        const string prefix = "/jobs/view/";
        if (uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            string id = uri.AbsolutePath[prefix.Length..].TrimEnd('/');
            if (IsNumericId(id))
                return id;
        }

        if (uri.AbsolutePath is "/jobs/search" or "/jobs/search/")
        {
            string? id = GetSingleQueryValue(uri, "currentJobId");
            if (id is not null && IsNumericId(id))
                return id;
        }

        return null;
    }

    private static bool IsNumericId(string value) =>
        value.Length is > 0 and <= 500 && value.All(char.IsAsciiDigit);

    private static string? GetSingleQueryValue(Uri uri, string key)
    {
        string? result = null;
        foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=', 2);
            if (pair.Length != 2 || Uri.UnescapeDataString(pair[0]) != key)
                continue;

            string value = Uri.UnescapeDataString(pair[1]);
            if (result is not null || value.Length is 0 or > 500 ||
                !value.All(char.IsAsciiLetterOrDigit))
                return null;
            result = value;
        }

        return result;
    }
}
