using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;

// Windows Imaging Component supplies the PNG/JPEG/BMP decoders already used by WPF.
// Only compressed snapshots and small thumbnails survive the import operation.
public sealed class ImagePreparationService(ImageLimits options) : IJobImageImporter
{
    public async Task<JobImageInput> ImportFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length <= 0 || file.Length > options.MaxFileBytes)
            throw new ArgumentException($"O arquivo deve ter até {options.MaxFileBytes / 1024 / 1024} MB.");
        byte[] bytes = new byte[checked((int)file.Length)];
        await file.ReadExactlyAsync(bytes, cancellationToken);
        return await ImportAsync(Path.GetFileName(path), bytes, cancellationToken);
    }

    public Task<JobImageInput> ImportAsync(string name, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bitmap = Decode(bytes);
        // Hash decoded pixels and dimensions: different encodings of the same print are duplicates,
        // but overlapping crops remain separate inputs.
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        byte[] row = new byte[stride];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(converted.PixelWidth));
        hash.AppendData(BitConverter.GetBytes(converted.PixelHeight));
        for (int y = 0; y < converted.PixelHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            converted.CopyPixels(new Int32Rect(0, y, converted.PixelWidth, 1), row, stride, 0);
            hash.AppendData(row);
        }
        double scale = Math.Min(1, (double)JobImageInput.ThumbnailMaxDimension / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        var thumbnail = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
        return new JobImageInput(Guid.NewGuid(), name.Length <= 255 ? name : name[..255], bytes.ToArray(),
            Convert.ToHexString(hash.GetHashAndReset()), Encode(thumbnail));
    }, cancellationToken);

    public BitmapSource Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > options.MaxFileBytes)
            throw new ArgumentException($"Imagem vazia ou maior que {options.MaxFileBytes / 1024 / 1024} MB.");
        var header = bytes.Span;
        bool png = header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        bool jpeg = header.StartsWith(new byte[] { 255, 216, 255 });
        bool bmp = header.StartsWith("BM"u8);
        bool webp = header.Length >= 12 && header.StartsWith("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8);
        if (!png && !jpeg && !bmp && !webp) throw new ArgumentException("Conteúdo não suportado. Use imagens PNG, JPEG, BMP ou WEBP com decodificador WIC instalado.");
        using var stream = new MemoryStream(bytes.ToArray(), false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        if ((!webp && decoder is not (PngBitmapDecoder or JpegBitmapDecoder or BmpBitmapDecoder)) || decoder.Frames.Count != 1)
            throw new ArgumentException("Use uma imagem estática PNG, JPEG, BMP ou WEBP com um único quadro.");
        var frame = decoder.Frames[0];
        options.ValidateDimensions(frame.PixelWidth, frame.PixelHeight);
        // Force decoding only after checking dimensions and detach from the input stream.
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int stride = checked(frame.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * frame.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    public (ReadOnlyMemory<byte> Bytes, long Pixels) Prepare(JobImageInput input, OllamaOptions limits, CancellationToken ct)
    {
        var bitmap = Decode(input.Content);
        ct.ThrowIfCancellationRequested();
        // Upscaling creates no new text detail and can quadruple the vision workload.
        // Preserve the original pixels; convert only formats that require normalization.
        ReadOnlyMemory<byte> bytes = input.Content.Span.StartsWith("BM"u8) || input.Content.Span.StartsWith("RIFF"u8)
            ? Encode(bitmap) : input.Content;
        long pixels = (long)bitmap.PixelWidth * bitmap.PixelHeight;
        if (bytes.Length > limits.MaxBatchImageBytes || pixels > limits.MaxBatchPixels)
            throw new ArgumentException("A captura excede os limites da IA. Recorte em partes legíveis ou ajuste os limites; o aplicativo não reduzirá texto pequeno para fazê-la caber.");
        return (bytes, pixels);
    }

    public static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
