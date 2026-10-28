using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;

namespace JobTracker.Modules.Settings.Application;

public sealed record ExportDocument(int Version, DateTime ExportedAt, IReadOnlyList<JobInfo> Jobs);
public sealed record ImportLine(int Number, string Title, string Result);
public sealed record ImportPreview(IReadOnlyList<JobInfo> Accepted, IReadOnlyList<ImportLine> Lines)
{
    public int Ignored => Lines.Count(l => l.Result.StartsWith("Ignorado", StringComparison.Ordinal));
    public int Rejected => Lines.Count(l => l.Result.StartsWith("Rejeitado", StringComparison.Ordinal));
    public string Summary => $"{Accepted.Count} aceitos • {Ignored} ignorados • {Rejected} rejeitados";
}
public sealed class DataTransferService(ITrackingRepository repository, TrackingService tracking, JobIdentifier identifier, ITransferFiles files)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 32,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter<ApplicationStatus>() } };
    public async Task ExportAsync(string path, bool csv, CancellationToken ct)
    {
        var jobs = await tracking.ListAsync(ct);
        string text = await Task.Run(() => csv ? Csv(jobs) : JsonSerializer.Serialize(new ExportDocument(1, DateTime.UtcNow, jobs), Json), ct);
        await files.WriteAsync(path, text, ct);
    }
    public async Task<ImportPreview> PreviewAsync(string path, bool csv, CancellationToken ct)
    {
        string text = await files.ReadAsync(path, ct);
        var existing = (await tracking.ListAsync(ct)).ToList();
        return await Task.Run(() => Preview(text, csv, existing, ct), ct);
    }
    private ImportPreview Preview(string text, bool csv, List<JobInfo> existing, CancellationToken ct)
    {
        var accepted = new List<JobInfo>(); var lines = new List<ImportLine>();
        using JsonDocument? document = csv ? null : JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        var jsonItems = new List<JsonElement>();
        var csvRows = csv ? CsvImport.Parse(text) : [];
        if (document is not null)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version) || !version.TryGetInt32(out int value) || value != 1 ||
                !root.TryGetProperty("Jobs", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Formato ou versão do JSON incompatível.");
            jsonItems.AddRange(items.EnumerateArray());
        }
        int count = csv ? csvRows.Count : jsonItems.Count;
        if (count > 10000) throw new InvalidDataException("O arquivo excede 10.000 registros.");
        for (int index = 0; index < count; index++)
        {
            ct.ThrowIfCancellationRequested();
            int number = lines.Count + 1;
            string title = "Registro " + number;
            try
            {
                JobInfo item = csv ? CsvImport.Convert(csvRows[index]) : jsonItems[index].Deserialize<JobInfo>(Json) ?? throw new InvalidDataException("Registro nulo.");
                title = item.Content.Title;
                Validate(item);
                string normalized = item.Url.Length == 0 ? item.NormalizedUrl : identifier.Identify(item.Url).NormalizedUrl;
                string? canonical = item.CanonicalUrl is null ? null : identifier.Identify(item.CanonicalUrl).NormalizedUrl;
                JobInfo candidate = item with { NormalizedUrl = normalized, CanonicalUrl = canonical,
                    BatchImportKey = item.BatchImportKey?.ToUpperInvariant(), BatchImageHashes = item.BatchImageHashes.ToUpperInvariant() };
                if (existing.Any(j => j.Id == item.Id || tracking.MatchesUrl(j, normalized) || canonical is not null && tracking.MatchesUrl(j, canonical) ||
                    candidate.BatchImportKey is not null && j.BatchImportKey == candidate.BatchImportKey || OverlapsBatchOrigin(j, candidate)))
                { lines.Add(new(number, item.Content.Title, "Ignorado: ID, URL ou origem de lote já cadastrados")); continue; }
                bool similar = existing.Any(j => TrackingService.Similar(j, item.Content));
                accepted.Add(candidate); existing.Add(candidate);
                lines.Add(new(number, item.Content.Title, similar ? "Aceito: empresa/cargo semelhantes; confirme que é outra oportunidade" : "Aceito"));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException or FormatException)
            { lines.Add(new(number, title, "Rejeitado: " + (ex is JsonException ? "Campos ausentes ou tipos inválidos." : ex.Message))); }
        }
        return new(accepted, lines);
    }
    public async Task ImportAsync(ImportPreview preview, CancellationToken ct)
    {
        // Recheck against current persisted records immediately before the atomic insert.
        var existing = await tracking.ListAsync(ct);
        if (preview.Accepted.Any(item => existing.Any(j => j.Id == item.Id || tracking.MatchesUrl(j, item.NormalizedUrl) || item.CanonicalUrl is not null && tracking.MatchesUrl(j, item.CanonicalUrl) ||
            item.BatchImportKey is not null && j.BatchImportKey == item.BatchImportKey || OverlapsBatchOrigin(j, item))))
            throw new InvalidOperationException("Os dados mudaram após a prévia. Reabra o arquivo para revisar as duplicidades.");
        await repository.ImportAsync(preview.Accepted.Select(ToJob).ToArray(), ct);
    }
    private void Validate(JobInfo item)
    {
        if (item.History.Any(e => e is null) || item.Appointments.Any(a => a is null)) throw new InvalidDataException("Histórico ou agenda contém um item nulo.");
        if (item.Id == Guid.Empty || !Enum.IsDefined(item.Status) || item.CreatedAt == default || item.CreatedAt.Kind != DateTimeKind.Utc || item.UpdatedAt.Kind != DateTimeKind.Utc || item.AppliedAt is { Kind: not DateTimeKind.Utc })
            throw new InvalidDataException("Identificador, status ou datas inválidos.");
        if (item.Url.Length == 0 && !item.NormalizedUrl.StartsWith("manual:", StringComparison.Ordinal)) throw new InvalidDataException("Vaga manual sem identificador.");
        if (item.Url.Length > 0) identifier.Identify(item.Url);
        if (item.Source.Length is 0 or > 100 || item.History.Count > 10000 || item.Appointments.Count > 1000) throw new InvalidDataException("Limites do registro excedidos.");
        _ = ToJob(item);
    }
    private static Job ToJob(JobInfo item)
    {
        var job = new Job(item.Source, item.ExternalJobId, item.Url, item.NormalizedUrl,
            item.Content.Company, item.Content.Title, item.Content.Description, item.AppliedAt ?? item.CreatedAt);
        job.Edit(item.Content); job.SetCapture(item.CanonicalUrl, item.ExtractionMetadata); job.SetOcrCapture(item.OcrCapture);
        if (item.BatchImportKey is not null)
            job.SetBatchOrigin(item.BatchImportKey, item.BatchImageHashes.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        else if (item.BatchImageHashes.Length > 0)
            throw new InvalidDataException("Hashes de screenshots sem identificação do lote.");
        var appointments = item.Appointments.Select(a =>
        {
            var result = new Appointment(a.Title, a.LocalDateTime, a.Kind, a.Notes);
            if (a.Completed) result.Complete();
            return result;
        }).ToArray();
        job.RestoreTracking(item.Id, item.Status, item.AppliedAt.HasValue, item.CreatedAt, item.UpdatedAt,
            item.IsDeleted, item.History.Select(e => new JobEvent(e.Description, e.OccurredAt)).ToArray(), appointments);
        return job;
    }
    private static bool OverlapsBatchOrigin(JobInfo left, JobInfo right) =>
        left.BatchImageHashes.Length > 0 && right.BatchImageHashes.Length > 0 &&
        left.BatchImageHashes.Split('\n').Intersect(right.BatchImageHashes.Split('\n'), StringComparer.OrdinalIgnoreCase).Any();
    private static string Csv(IReadOnlyList<JobInfo> jobs)
    {
        static string Cell(string value)
        {
            string safe = value;
            string trimmed = value.TrimStart();
            if (trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || value[0] is '\t' or '\r' or '\n')) safe = "'" + value;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }
        var result = new StringBuilder("Id;URL;Empresa;Cargo;Status;CandidaturaUTC;CriacaoUTC;Local;Modalidade;Senioridade;Contrato;Remuneracao;Tecnologias;Descricao;Requisitos;Notas;Canal;ProximaAcao;DataProximaAcao;Lixeira;Origem\r\n");
        foreach (var j in jobs)
            result.AppendLine(string.Join(";", new[] { j.Id.ToString(), j.Url, j.Content.Company, j.Content.Title, j.StatusLabel,
                j.AppliedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "", j.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                j.Content.Location, j.Content.WorkMode, j.Content.Seniority, j.Content.Contract, j.Content.Salary, j.Content.Technologies,
                j.Content.Description, j.Content.Requirements, j.Content.Notes, j.Content.Channel, j.Content.NextAction,
                j.Content.NextActionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", j.IsDeleted.ToString(), j.Source }.Select(Cell)));
        return result.ToString();
    }
}
