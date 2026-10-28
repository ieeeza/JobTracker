using System.Globalization;
using System.IO;
using System.Text;
using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;

namespace JobTracker.Modules.Settings.Application;

internal static class CsvImport
{
    // Accept the documented UTF-8, semicolon-delimited export format, including quoted multiline fields.
    public static List<IReadOnlyDictionary<string, string>> Parse(string content)
    {
        var rows = new List<string[]>(); var cells = new List<string>(); var field = new StringBuilder();
        bool quoted = false, afterQuote = false;
        for (int index = 0; index < content.Length; index++)
        {
            char current = content[index];
            if (quoted)
            {
                if (current == '"' && index + 1 < content.Length && content[index + 1] == '"') { field.Append('"'); index++; }
                else if (current == '"') { quoted = false; afterQuote = true; }
                else field.Append(current);
            }
            else if (current == '"' && field.Length == 0 && !afterQuote) quoted = true;
            else if (current is ';' or '\r' or '\n')
            {
                cells.Add(field.ToString()); field.Clear(); afterQuote = false;
                if (current == ';') continue;
                if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                rows.Add(cells.ToArray()); cells.Clear();
            }
            else
            {
                if (afterQuote || current == '"') throw new InvalidDataException("CSV com aspas inválidas.");
                field.Append(current);
            }
        }
        if (quoted) throw new InvalidDataException("CSV com campo entre aspas não finalizado.");
        if (field.Length > 0 || cells.Count > 0 || afterQuote) { cells.Add(field.ToString()); rows.Add(cells.ToArray()); }
        if (rows.Count == 0) throw new InvalidDataException("CSV vazio.");
        string[] header = rows[0];
        if (!header.Contains("Cargo") || !header.Contains("Empresa") || header.Distinct().Count() != header.Length)
            throw new InvalidDataException("Use o CSV exportado pelo JobTracker, com cabeçalhos Cargo e Empresa e separador ponto e vírgula.");
        return rows.Skip(1).Where(r => r.Any(v => v.Length > 0)).Select(row =>
        {
            if (row.Length != header.Length) throw new InvalidDataException("Quantidade de colunas inconsistente no CSV.");
            return (IReadOnlyDictionary<string, string>)header.Zip(row).ToDictionary(p => p.First, p => UnescapeFormula(p.Second));
        }).ToList();
    }
    private static string UnescapeFormula(string value) => value.StartsWith('\'') && value.Length > 1 &&
        ("=+-@".Contains(value[1..].TrimStart().FirstOrDefault()) || value[1] is '\t' or '\r' or '\n') ? value[1..] : value;
    public static JobInfo Convert(IReadOnlyDictionary<string, string> row)
    {
        string Get(string key) => row.GetValueOrDefault(key) ?? "";
        DateTime? Date(string key, bool utc)
        {
            string text = Get(key);
            if (text.Length == 0) return null;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) throw new InvalidDataException("Data inválida: " + key);
            if (utc && date.Kind != DateTimeKind.Utc) throw new InvalidDataException("A data deve estar em UTC: " + key);
            return date;
        }
        Guid id = Get("Id").Length == 0 ? Guid.NewGuid() : Guid.Parse(Get("Id"));
        string status = Get("Status");
        var option = StatusCatalog.All.SingleOrDefault(s => s.Label == status);
        if (option is null && status.Length > 0) throw new InvalidDataException("Status desconhecido.");
        DateTime created = Date("CriacaoUTC", true) ?? DateTime.UtcNow;
        var content = new JobContent(Get("Cargo"), Get("Empresa"), Get("Descricao"), Get("Local"), Get("Modalidade"),
            Get("Senioridade"), Get("Contrato"), Get("Remuneracao"), Get("Requisitos"), Get("Notas"), Get("Canal"),
            Get("ProximaAcao"), Date("DataProximaAcao", false), Get("Tecnologias"));
        if (Get("Lixeira").Length > 0 && !bool.TryParse(Get("Lixeira"), out _)) throw new InvalidDataException("Valor de Lixeira inválido.");
        return new(id, Guid.NewGuid(), Get("URL"), "manual:" + id.ToString("N"), null, Get("Origem").Length == 0 ? "Importação CSV" : Get("Origem"), null,
            content, option?.Value ?? ApplicationStatus.Saved, Date("CandidaturaUTC", true), created, DateTime.UtcNow,
            bool.TryParse(Get("Lixeira"), out bool deleted) && deleted, "Importado de CSV; histórico anterior não incluído no CSV.",
            [new(DateTime.UtcNow, "Importado de CSV")], []);
    }
}
