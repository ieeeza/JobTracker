using System.Text.RegularExpressions;

namespace JobTracker.Modules.JobAnalysis.Application.Services;

public static class TechnologyCatalog
{
    public static string NormalizeNames(string value) => string.Join(", ", value.Split([',', ';'],
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(name =>
        {
            // Only a complete alias is replaced; versioned and unknown names remain intact.
            var entry = Entries.FirstOrDefault(e => Regex.IsMatch(name, "^(?:" + e.Pattern + ")$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            return entry?.Name ?? name;
        }).Distinct(StringComparer.OrdinalIgnoreCase));

    // More specific aliases claim their spans first (React Native != React, Java != JavaScript).
    private sealed record Technology(string Name, string Pattern);
    private static readonly Technology[] Entries =
    [
        new("ASP.NET Core", @"ASP[.\s-]*NET\s+Core"), new("ASP.NET", @"ASP[.\s-]*NET"),
        new("React Native", @"React\s+Native"), new("React", @"React(?:[.\s-]*JS)?"),
        new("C#", @"C\s*#|C[\s-]*Sharp"), new("C++", @"C\+\+"),
        new(".NET", @"\.NET(?:\s+Core)?|Dot[\s-]*net"), new("Node.js", @"Node[.\s-]*JS"),
        new("PostgreSQL", @"Postgre\s*SQL|Postgres"), new("SQL Server", @"(?:Microsoft\s+)?SQL\s+Server"),
        new("JavaScript", @"JavaScript"), new("TypeScript", @"TypeScript"), new("Java", @"Java"),
        new("Python", @"Python"), new("Kotlin", @"Kotlin"), new("Go", @"Golang|Go"), new("Rust", @"Rust"),
        new("PHP", @"PHP"), new("Ruby", @"Ruby"), new("Swift", @"Swift"), new("Dart", @"Dart"),
        new("Angular", @"Angular(?:JS)?"), new("Vue", @"Vue(?:[.\s]*JS)?"), new("Next.js", @"Next[.\s]*JS"),
        new("Spring Boot", @"Spring\s+Boot"), new("Spring", @"Spring"), new("Django", @"Django"),
        new("Flask", @"Flask"), new("FastAPI", @"Fast\s*API"), new("NestJS", @"Nest[.\s]*JS"),
        new("Laravel", @"Laravel"), new("Flutter", @"Flutter"), new("Android", @"Android"), new("iOS", @"iOS"),
        new("HTML", @"HTML(?:5)?"), new("Tailwind CSS", @"Tailwind(?:\s+CSS)?"), new("CSS", @"CSS(?:3)?"),
        new("CI/CD", @"CI\s*/\s*CD|continuous integration(?: and continuous delivery)?"),
        new("Entity Framework", @"Entity\s+Framework(?:\s+Core)?|EF\s+Core"),
        new("MySQL", @"MySQL"), new("SQLite", @"SQLite"), new("MongoDB", @"Mongo\s*DB"), new("Redis", @"Redis"), new("SQL", @"SQL"),
        new("Docker", @"Docker"), new("Kubernetes", @"Kubernetes|K8s"), new("Terraform", @"Terraform"),
        new("Azure DevOps", @"Azure\s+DevOps"), new("Azure", @"Azure"), new("AWS", @"AWS|Amazon\s+Web\s+Services"),
        new("Google Cloud", @"Google\s+Cloud|GCP"), new("GitHub Actions", @"GitHub\s+Actions"),
        new("GitLab CI", @"GitLab\s+CI"), new("Jenkins", @"Jenkins"), new("Git", @"Git"),
        new("RabbitMQ", @"RabbitMQ"), new("Kafka", @"(?:Apache\s+)?Kafka"), new("Linux", @"Linux")
    ];

    public static string Extract(string text)
    {
        var claimed = new List<(int Start, int End)>();
        var found = new List<string>();
        foreach (var technology in Entries)
        {
            bool evidence = false;
            foreach (Match match in Regex.Matches(text, @"(?<![\p{L}\p{N}])(?:" + technology.Pattern + @")(?![\p{L}\p{N}])",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            {
                if (claimed.Any(span => match.Index < span.End && match.Index + match.Length > span.Start)) continue;
                if ((match.Value.Equals("go", StringComparison.OrdinalIgnoreCase) || match.Value.Equals("react", StringComparison.OrdinalIgnoreCase)) &&
                    !HasTechnicalContext(text, match)) continue;
                claimed.Add((match.Index, match.Index + match.Length)); evidence = true;
            }
            if (evidence) found.Add(technology.Name);
        }
        return string.Join(", ", found);
    }

    private static bool HasTechnicalContext(string text, Match match)
    {
        int start = text.LastIndexOf('\n', match.Index) + 1;
        int end = text.IndexOf('\n', match.Index);
        if (end < 0) end = text.Length;
        string line = text[start..end];
        string suffix = text[match.Index..Math.Min(end, match.Index + 50)];
        if (Regex.IsMatch(suffix, @"^(?:go\s+(?:to|home|beyond|above|the extra)|react\s+(?:to|quickly|promptly))\b",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) return false;
        if (line.Trim(' ', '-', '*', '•').Equals(match.Value, StringComparison.OrdinalIgnoreCase))
        {
            string preceding = text[Math.Max(0, start - 250)..start];
            if (Regex.IsMatch(preceding, @"(?:^|\n)\s*(?:requisitos|requirements|tecnologias|technologies|stack|linguagens|languages)\s*:?\s*(?:\n|$)",
                RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))) return true;
        }
        return Regex.IsMatch(line, @"\b(?:linguagem|language|programming|framework|stack|tecnologias?|technologies|backend|frontend|developer|desenvolvedor|desenvolvimento|experi[eê]ncia|experience)\b|[,;|]|\b(?:Java|Python|TypeScript|JavaScript|Docker|Node\.js|Golang)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
