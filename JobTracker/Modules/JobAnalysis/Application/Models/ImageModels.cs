using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobAnalysis.Application.Models;

public sealed record JobImageInput(Guid Id, string Name, ReadOnlyMemory<byte> Content,
    string Fingerprint, ReadOnlyMemory<byte> Thumbnail)
{
    public const int ThumbnailMaxDimension = 220;
    // Include a conservative allowance for the decoded WPF thumbnail, not just PNG bytes.
    public long EstimatedMemoryBytes => (long)Content.Length + Thumbnail.Length + 4L * ThumbnailMaxDimension * ThumbnailMaxDimension;
}
public enum ImageAnalysisStatus { Pending, Processing, Completed, Failed }
public sealed record ImageAnalysisProgress(Guid ImageId, int Current, int Total, ImageAnalysisStatus Status, string Message);

public sealed record ImageLimits
{
    public long MaxFileBytes { get; init; } = 20 * 1024 * 1024;
    public long MaxBatchBytes { get; init; } = 256 * 1024 * 1024;
    public int MaxDimension { get; init; } = 16000;
    public long MaxPixels { get; init; } = 24000000;
    public int MaxAspectRatio { get; init; } = 80;

    public void Validate()
    {
        if (MaxFileBytes is < 1024 or > 100 * 1024 * 1024 ||
            MaxBatchBytes < MaxFileBytes || MaxBatchBytes > 1024L * 1024 * 1024 ||
            MaxDimension is < 100 or > 32000 || MaxPixels is < 10000 or > 64000000 || MaxAspectRatio is < 2 or > 200)
            throw new ArgumentException("Limites de imagens inválidos. Revise os limites documentados.");
    }

    public void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension ||
            (long)width * height > MaxPixels || Math.Max(width, height) / (double)Math.Min(width, height) > MaxAspectRatio)
            throw new ArgumentException($"Dimensões não permitidas: {width} × {height}. Limites: {MaxDimension} por lado, {MaxPixels:N0} pixels e proporção {MaxAspectRatio}:1.");
    }
}
