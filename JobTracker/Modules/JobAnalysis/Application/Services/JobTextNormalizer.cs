using System.Text.RegularExpressions;

namespace JobTracker.Modules.JobAnalysis.Application.Services;

internal static class JobTextNormalizer
{
    private static readonly HashSet<string> Navigation = new(StringComparer.Ordinal)
    {
        "entrar", "cadastre-se", "login", "sign in", "sign up", "home", "inicio", "menu",
        "candidatar-se", "candidatura simplificada", "apply", "apply now", "salvar vaga",
        "save job", "compartilhar", "share", "voltar", "aceitar cookies", "aceitar todos os cookies"
    };
    private static readonly HashSet<string> FooterHeadings = new(StringComparer.Ordinal)
    {
        "vagas recomendadas", "vagas semelhantes", "outras vagas", "similar jobs", "recommended jobs",
        "politica de privacidade", "privacy policy", "termos de uso", "terms of service"
    };

    public static string Normalize(string text, CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            string line = Regex.Replace(raw.Replace('\u00a0', ' ').Replace("\u200b", ""), @"[\t ]+", " ",
                RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
            string key = JobTextKey.Normalize(line.TrimEnd(':'));
            if (Navigation.Contains(key)) continue;
            // Only a standalone footer heading ends the captured job; sentences remain intact.
            if (lines.Count > 5 && FooterHeadings.Contains(key)) break;
            if (line.Length > 0 && (lines.Count == 0 || lines[^1] != line)) lines.Add(line);
        }
        return string.Join('\n', lines);
    }
}
