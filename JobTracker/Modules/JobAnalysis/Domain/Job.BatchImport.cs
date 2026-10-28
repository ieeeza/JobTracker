namespace JobTracker.Modules.JobAnalysis.Domain;

public partial class Job
{
    public string? BatchImportKey { get; private set; }
    public string BatchImageHashes { get; private set; } = "";

    public void SetBatchOrigin(string key, IReadOnlyList<string> hashes)
    {
        if (BatchImportKey is not null) throw new InvalidOperationException("A origem do lote já está definida.");
        AssignBatchOrigin(key, hashes);
    }

    public void CorrectBatchOrigin(string key, IReadOnlyList<string> hashes)
    {
        if (BatchImportKey is null) throw new InvalidOperationException("Este registro não possui origem em lote.");
        string previousKey = BatchImportKey, previousHashes = BatchImageHashes;
        AssignBatchOrigin(key, hashes);
        if (previousKey != BatchImportKey || previousHashes != BatchImageHashes)
            Record("Origem do lote corrigida após revisão do agrupamento");
    }

    private void AssignBatchOrigin(string key, IReadOnlyList<string> hashes)
    {
        static bool ValidHash(string hash) => hash.Length == 64 && hash.All(char.IsAsciiHexDigit);
        if (!ValidHash(key) || hashes.Count is < 1 or > 10000 || hashes.Any(h => !ValidHash(h)))
            throw new ArgumentException("Identificação de origem do lote inválida.");
        BatchImportKey = key.ToUpperInvariant();
        BatchImageHashes = string.Join('\n', hashes.Select(h => h.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }
}
