using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using JobTracker.Modules.JobAnalysis.Application.BatchImport;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;

public sealed class ScreenshotDiscovery(IJobImageImporter importer, ImageLimits limits) : IScreenshotDiscovery
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
    public Task<ImportBatch> DiscoverAsync(string folder, BatchImportOptions options, CancellationToken ct) => Task.Run(async () =>
    {
        options.Validate();
        string fullPath = Path.GetFullPath(folder);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("A pasta selecionada não existe ou não está acessível.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var batch = new ImportBatch { Folder = fullPath, Options = options };
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = options.IncludeSubdirectories,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };
        foreach (string path in Directory.EnumerateFiles(fullPath, "*", enumeration))
        {
            ct.ThrowIfCancellationRequested();
            if (!Extensions.Contains(Path.GetExtension(path))) continue;
            if (batch.Images.Count >= options.MaxFiles) throw new ArgumentException($"A pasta excede o limite de {options.MaxFiles} imagens. Ajuste o limite ou selecione uma pasta menor.");
            var image = new BatchScreenshot { Path = path, Name = Path.GetFileName(path) };
            batch.Images.Add(image);
            try
            {
                // Validate one file at a time. Compressed bytes and decoded pixels never accumulate across the folder.
                var input = await importer.ImportFileAsync(path, ct);
                image.Hash = Convert.ToHexString(SHA256.HashData(input.Content.Span));
                image.FileBytes = input.Content.Length;
                (image.CapturedAtUtc, image.TimeSource) = CaptureTime(path, input.Content);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException)
            { image.Error = ex is ArgumentException ? ex.Message : "Imagem inacessível, corrompida ou sem decodificador compatível."; }
        }
        bool useCaptureTime = batch.Images.All(i => i.TimeSource == "timestamp no nome") || batch.Images.All(i => i.TimeSource == "metadado de captura");
        batch.Images.Sort((left, right) => Compare(left, right, useCaptureTime));
        for (int index = 0; index < batch.Images.Count; index++) batch.Images[index].Order = index;
        batch.Fingerprint = ImportBatch.Hash(batch.Images.Select(i => i.Hash.Length > 0 ? i.Hash : "invalid:" + i.Path));
        batch.Metrics.DiscoveryMilliseconds = watch.Elapsed.TotalMilliseconds;
        batch.Metrics.TotalMilliseconds = batch.Metrics.DiscoveryMilliseconds;
        if (batch.Images.Count == 0) throw new ArgumentException("Nenhuma imagem PNG, JPEG, BMP ou WEBP encontrada na pasta.");
        batch.Notices.Add("Originais preservados. A data de modificação não foi usada como data de captura. Confira a ordem antes de iniciar.");
        return batch;
    }, ct);

    public async Task VerifyAsync(BatchScreenshot image, CancellationToken ct)
    {
        await using var stream = new FileStream(image.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (stream.Length <= 0 || stream.Length > limits.MaxFileBytes ||
            Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)) != image.Hash)
            throw new IOException("O screenshot mudou desde a descoberta. Selecione a pasta novamente para importar a nova versão.");
    }

    public async Task<JobImageInput> LoadVerifiedAsync(BatchScreenshot image, CancellationToken ct)
    {
        var info = new FileInfo(image.Path);
        if (!info.Exists || info.Length <= 0 || info.Length > limits.MaxFileBytes || !Extensions.Contains(info.Extension))
            throw new IOException("Screenshot ausente, incompatível ou fora do limite de tamanho.");
        var input = await importer.ImportFileAsync(image.Path, ct);
        if (Convert.ToHexString(SHA256.HashData(input.Content.Span)) != image.Hash)
            throw new IOException("O screenshot mudou desde a descoberta. Selecione a pasta novamente para importar a nova versão.");
        return input;
    }

    private static (DateTime, string) CaptureTime(string path, ReadOnlyMemory<byte> bytes)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        var match = Regex.Match(stem, @"(?<y>20\d{2})[-_.]?(?<m>\d{2})[-_.]?(?<d>\d{2})[T _-]+(?<h>\d{2})[-_.:]?(?<n>\d{2})[-_.:]?(?<s>\d{2})", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (match.Success && DateTime.TryParseExact(string.Concat(new[] { "y", "m", "d", "h", "n", "s" }.Select(n => match.Groups[n].Value)),
                "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var named))
            return (DateTime.SpecifyKind(named, DateTimeKind.Local).ToUniversalTime(), "timestamp no nome");
        using var stream = new MemoryStream(bytes.ToArray(), false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        try
        {
            if (decoder.Frames[0].Metadata is BitmapMetadata metadata && DateTime.TryParse(metadata.DateTaken, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var taken))
                return (taken.ToUniversalTime(), "metadado de captura");
        }
        catch (NotSupportedException)
        { return (File.GetCreationTimeUtc(path), "metadados indisponíveis / ordem natural"); }
        return (File.GetCreationTimeUtc(path), "ordem natural / criação do arquivo (fallback)");
    }

    private static int Compare(BatchScreenshot left, BatchScreenshot right, bool useCaptureTime)
    {
        int result = useCaptureTime ? left.CapturedAtUtc.CompareTo(right.CapturedAtUtc) : NaturalCompare(left.Name, right.Name);
        if (result == 0) result = left.CapturedAtUtc.CompareTo(right.CapturedAtUtc);
        return result != 0 ? result : StringComparer.Ordinal.Compare(left.Path, right.Path);
    }

    private static int NaturalCompare(string left, string right)
    {
        var first = Regex.Matches(left, @"\d+|\D+", RegexOptions.None, TimeSpan.FromSeconds(1));
        var second = Regex.Matches(right, @"\d+|\D+", RegexOptions.None, TimeSpan.FromSeconds(1));
        for (int i = 0; i < Math.Min(first.Count, second.Count); i++)
        {
            string a = first[i].Value, b = second[i].Value;
            int result;
            if (char.IsAsciiDigit(a[0]) && char.IsAsciiDigit(b[0]))
            {
                a = a.TrimStart('0'); b = b.TrimStart('0');
                result = a.Length.CompareTo(b.Length);
                if (result == 0) result = StringComparer.Ordinal.Compare(a, b);
            }
            else result = StringComparer.OrdinalIgnoreCase.Compare(a, b);
            if (result != 0) return result;
        }
        return first.Count.CompareTo(second.Count);
    }
}
