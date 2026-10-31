using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

namespace JobTracker.Tests;

internal static class OllamaTestData
{
    public static OllamaJobData Job => new()
    {
        Title = "Desenvolvedor .NET",
        Company = "Empresa de exemplo",
        Seniority = "Sênior",
        WorkMode = "Remoto",
        Location = "Brasil",
        EmploymentType = "CLT, tempo integral",
        Salary = "R$ 10.000 a R$ 12.000",
        Description = "Desenvolvimento de sistemas para gestão de candidaturas.",
        Responsibilities = ["Implementar APIs e revisar código."],
        Requirements = ["Experiência com C# e SQL."],
        PreferredRequirements = ["Conhecimento de mensageria."],
        Benefits = ["Plano de saúde.", "Vale-alimentação."],
        Technologies = ["C#", "SQL"],
        Url = "https://example.com/vagas/123",
        MultipleJobs = false
    };

    public static string Json => JsonSerializer.Serialize(Job, OllamaExtractionContract.ExtractionJsonOptions);
}
