using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infraestructure.Ollama;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

internal sealed record OllamaPdfBlock(string Text, string Prefix, OllamaPdfContextBudget Budget, int ContextTokens);

internal sealed record OllamaPdfPlan(IReadOnlyList<OllamaPdfBlock> Blocks, PdfAnalysisEstimate Estimate)
{
    public const int AutomaticMaximumContext = 8192;
    public const int MaximumCharacters = 120000;
    public const int MaximumBlocks = 12;
    public const int MaximumSeconds = 900;

    public static OllamaOptions LimitOptions(OllamaOptions options, int modelContext = AutomaticMaximumContext)
    {
        int maximum = Math.Min(Math.Min(options.MaxContextTokens, AutomaticMaximumContext), modelContext);
        if (maximum < 2048) throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
            "O modelo não oferece contexto suficiente para este contrato de extração (mínimo 2.048 tokens).");
        return options with { ContextTokens = Math.Min(4096, maximum), MaxContextTokens = maximum };
    }

    public static OllamaPdfPlan Create(string text, OllamaOptions options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (text.Length > MaximumCharacters) throw LimitExceeded();
        options = LimitOptions(options);
        var full = OllamaPdfContextBudget.Estimate(text);
        var blocks = new List<OllamaPdfBlock>();
        if (full.RequiredTokens <= options.MaxContextTokens)
            blocks.Add(new(text, OllamaPdfExtractionContract.UserPrefix, full, full.SelectContextTokens(options)));
        else
        {
            int start = 0;
            string section = "";
            while (start < text.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (blocks.Count == MaximumBlocks) throw LimitExceeded();
                string prefix = OllamaPdfExtractionContract.FragmentPrefix +
                    (section.Length == 0 ? "" : $"Último cabeçalho explícito antes do trecho: {section}\n") + "\n";
                int low = 1, high = text.Length - start, length = 0;
                while (low <= high)
                {
                    ct.ThrowIfCancellationRequested();
                    int middle = low + (high - low) / 2;
                    // Short/whitespace prefixes still consume context; validate meaningful text at document level.
                    string candidate = text.Substring(start, middle);
                    var budget = OllamaPdfContextBudget.Estimate(candidate + "a", prefix, fragment: true);
                    if (budget.RequiredTokens <= options.MaxContextTokens) { length = middle; low = middle + 1; }
                    else high = middle - 1;
                }
                if (length == 0) throw LimitExceeded();
                int end = start + length;
                if (end < text.Length) end = FindBoundary(text, start, end);
                string part = text[start..end];
                if (string.IsNullOrWhiteSpace(part)) throw LimitExceeded();
                var partBudget = OllamaPdfContextBudget.Estimate(part, prefix, fragment: true);
                blocks.Add(new(part, prefix, partBudget, partBudget.SelectContextTokens(options)));
                section = LastSection(part, section);
                start = end;
            }
        }
        return new(blocks.AsReadOnly(), new(text.Length, full.TextBytes, full.TextTokens, full.RequiredTokens,
            blocks.Max(block => block.ContextTokens), options.MaxContextTokens, blocks.Count));
    }

    private static int FindBoundary(string text, int start, int end)
    {
        int minimum = start + (end - start) / 2;
        for (int index = end - 1; index > minimum; index--)
            if (text[index] == '\n' && text[index - 1] is '\n' or '\r') return index + 1;
        for (int index = end - 1; index > minimum; index--)
            if (text[index] == '\n') return index + 1;
        for (int index = end - 1; index > start; index--)
            if (char.IsWhiteSpace(text[index])) return index + 1;
        throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
            "Há um trecho sem separadores que não cabe no contexto. Revise o texto no editor; nenhum trecho foi cortado.");
    }

    private static string LastSection(string text, string previous)
    {
        foreach (string line in text.Split('\n'))
        {
            string heading = line.Trim().TrimEnd(':').ToLowerInvariant();
            if (heading is "requisitos" or "requisitos obrigatórios" or "requisitos desejáveis" or "desejáveis" or
                "diferenciais" or "benefícios" or "responsabilidades" or "qualificações" or "descrição da vaga" or
                "requirements" or "preferred qualifications" or "benefits" or "responsibilities") previous = heading;
        }
        return previous;
    }

    private static ImageAnalysisException LimitExceeded() => new(ImageAnalysisFailure.ContextExhausted,
        $"A análise segura aceita até {MaximumCharacters:N0} caracteres e {MaximumBlocks} blocos. " +
        "Reduza o texto no editor para uma única vaga. Nenhum resultado parcial será apresentado como completo.");
}
