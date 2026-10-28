namespace JobTracker.Modules.JobAnalysis.Domain;

public sealed record OcrImageText(string Name, string Text, float Quality, bool Preprocessed, string? Error);
public sealed record OcrCapture(DateTime ImportedAtUtc, string Text, string Source, string JobReference,
    IReadOnlyList<OcrImageText> Images)
{
    public void Validate()
    {
        if (ImportedAtUtc == default || ImportedAtUtc.Kind != DateTimeKind.Utc || Text is null ||
            Source is null || JobReference is null || Images is null || Text.Length > 200000 ||
            Source.Length > 100 || JobReference.Length > 500 || Images.Count > 10000)
            throw new ArgumentException("A captura OCR possui campos ou limites inválidos.");
        long total = Text.Length;
        foreach (var image in Images)
        {
            if (image is null || image.Name is null || image.Text is null || image.Name.Length > 255 ||
                image.Text.Length > 200000 || image.Error?.Length > 1000 ||
                !float.IsFinite(image.Quality) || image.Quality is < 0 or > 1)
                throw new ArgumentException("O texto individual da captura OCR é inválido.");
            total += image.Text.Length;
        }
        if (total > 1000000) throw new ArgumentException("O texto OCR excede 1.000.000 de caracteres. Remova prints sem relação com a vaga.");
    }
}

public partial class Job
{
    public OcrCapture? OcrCapture { get; private set; }

    public void SetOcrCapture(OcrCapture? capture)
    {
        capture?.Validate();
        OcrCapture = capture;
    }
}
