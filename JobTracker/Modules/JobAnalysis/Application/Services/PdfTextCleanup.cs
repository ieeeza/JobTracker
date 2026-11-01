namespace JobTracker.Modules.JobAnalysis.Application.Services;

/// <summary>Optional removal of exact, standalone portal controls; uncertain prose is retained.</summary>
public static class PdfTextCleanup
{
    private static readonly HashSet<string> AdvertisingControls = new(StringComparer.OrdinalIgnoreCase)
    {
        "Preferências de anúncios", "Gerenciar preferências de cookies", "Gerenciar cookies",
        "Ad preferences", "Manage cookie preferences"
    };

    private static readonly string[] LinkedInNavigation = ["Início", "Minha rede", "Vagas", "Mensagens", "Notificações"];

    public static string Clean(string text)
    {
        string[] lines = text.Split('\n');
        var repeatedLinks = lines.Select(line => line.Trim()).Where(IsNavigationLink)
            .GroupBy(line => line, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var retained = new List<string>(lines.Length);
        for (int index = 0; index < lines.Length; index++)
        {
            // A full known toolbar is safer than deleting isolated words such as "Vagas".
            if (index + LinkedInNavigation.Length <= lines.Length &&
                LinkedInNavigation.Select((label, offset) =>
                    string.Equals(lines[index + offset].Trim(), label, StringComparison.OrdinalIgnoreCase)).All(matches => matches))
            {
                index += LinkedInNavigation.Length - 1;
                continue;
            }
            string trimmed = lines[index].Trim();
            if (!AdvertisingControls.Contains(trimmed) && !repeatedLinks.Contains(trimmed)) retained.Add(lines[index]);
        }
        return string.Join('\n', retained);
    }

    private static bool IsNavigationLink(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host is "www.linkedin.com" or "linkedin.com" && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath is "/feed/" or "/mynetwork/" or "/messaging/" or "/notifications/";
}
