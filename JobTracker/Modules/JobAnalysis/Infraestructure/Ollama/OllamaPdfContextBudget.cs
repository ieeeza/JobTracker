using System.Text;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infraestructure.Ollama;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

internal sealed record OllamaPdfContextBudget(int TextBytes, long TextTokens, long SystemPromptTokens,
    long PrefixTokens, long OutputTokens, long SafetyTokens)
{
    // Measured Qwen input averaged >4 UTF-8 bytes/token on Portuguese job text + instructions.
    // Use 3 for Latin prose, byte fallback for other scripts/symbols and a further 20% margin.
    // This is an estimate, not the selected model's tokenizer. See docs/pdf-contexto-desempenho.md.
    private const int ProseBytesPerToken = 3;
    private const int MinimumOutputTokens = 512;
    private const int OutputStructureTokens = 256;
    private const int SafetyPercent = 20;
    public const int MessageOverheadTokens = 96;
    public const int ContextStepTokens = 4096;

    // format is a decoding constraint, not a second copy in the message content.
    public int SchemaPromptTokens => 0;
    public long InputTokens => TextTokens + SystemPromptTokens + PrefixTokens + MessageOverheadTokens;
    public long RequiredTokens => InputTokens + OutputTokens + SafetyTokens;

    public static OllamaPdfContextBudget Estimate(string text, string? prefix = null, bool fragment = false)
    {
        if (string.IsNullOrWhiteSpace(text) || !fragment && text.Count(char.IsLetter) < 20)
            throw new ArgumentException("O PDF não contém texto suficiente para identificar uma vaga. Confira o texto extraído.");

        long textTokens = EstimateTokens(text);
        long systemTokens = EstimateTokens(OllamaPdfExtractionContract.SystemPrompt);
        long prefixTokens = EstimateTokens(prefix ?? OllamaPdfExtractionContract.UserPrefix);
        long outputTokens = Math.Max(MinimumOutputTokens, textTokens + OutputStructureTokens);
        long subtotal = textTokens + systemTokens + prefixTokens + MessageOverheadTokens + outputTokens;
        long safetyTokens = (subtotal * SafetyPercent + 99) / 100;
        return new(Encoding.UTF8.GetByteCount(text), textTokens, systemTokens, prefixTokens, outputTokens, safetyTokens);
    }

    public static int SelectContextTokens(string text, OllamaOptions options) => Estimate(text).SelectContextTokens(options);

    public int SelectContextTokens(OllamaOptions options)
    {
        options.Validate();
        if (RequiredTokens > options.MaxContextTokens)
            throw new ImageAnalysisException(ImageAnalysisFailure.ContextExhausted,
                $"O PDF precisa de aproximadamente {RequiredTokens:N0} tokens para leitura e resposta completa, " +
                $"acima do máximo configurado de {options.MaxContextTokens:N0}. " +
                "O texto original foi preservado e não foi enviado. Use um documento menor de uma única vaga " +
                "ou ajuste o contexto de PDFs conforme a memória disponível. A estimativa varia com o modelo.");

        if (RequiredTokens <= Math.Min(ContextStepTokens, options.MaxContextTokens))
            return Math.Min(ContextStepTokens, options.MaxContextTokens);
        long rounded = ((RequiredTokens + ContextStepTokens - 1) / ContextStepTokens) * ContextStepTokens;
        return checked((int)Math.Min(rounded, options.MaxContextTokens));
    }

    private static long EstimateTokens(string text)
    {
        long proseBytes = 0;
        long fallbackBytes = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value <= 0x024f && (Rune.IsLetter(rune) || Rune.IsWhiteSpace(rune)))
                proseBytes += rune.Utf8SequenceLength;
            else
                fallbackBytes += rune.Utf8SequenceLength;
        }
        return (proseBytes + ProseBytesPerToken - 1) / ProseBytesPerToken + fallbackBytes;
    }
}
