using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JobTracker.Modules.JobAnalysis.Application.Models;
using Microsoft.Win32;

namespace JobTracker.Presentation;

public sealed class DesktopImageServices
{
    public string? SelectFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Selecionar pasta de screenshots", Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    public IReadOnlyList<string> SelectFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Adicionar prints da vaga", Multiselect = true, CheckFileExists = true,
            Filter = "Imagens PNG, JPEG e BMP|*.png;*.jpg;*.jpeg;*.bmp"
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public async Task<byte[]> ReadClipboardAsync(ImageLimits options, CancellationToken ct)
    {
        if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } image)
            throw new ArgumentException("A área de transferência não contém uma imagem. Use Win + Shift + S para capturar um print.");
        options.ValidateDimensions(image.PixelWidth, image.PixelHeight);
        image.Freeze();
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream(); encoder.Save(stream);
            if (stream.Length > options.MaxFileBytes) throw new ArgumentException("O print colado excede o tamanho configurado por imagem.");
            ct.ThrowIfCancellationRequested();
            return stream.ToArray();
        }, ct);
    }

    public static BitmapSource LoadThumbnail(ReadOnlyMemory<byte> bytes) => Load(bytes, JobImageInput.ThumbnailMaxDimension);

    private static BitmapSource Load(ReadOnlyMemory<byte> bytes, int width)
    {
        using var stream = new MemoryStream(bytes.ToArray(), false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        double scale = Math.Min(1, width / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = Math.Max(1, (int)(frame.PixelWidth * scale));
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }

    public async Task PreviewAsync(JobImageInput input, CancellationToken ct)
    {
        var source = await Task.Run(() => Load(input.Content, 1600), ct);
        ct.ThrowIfCancellationRequested();
        var window = new Window
        {
            Title = input.Name, Owner = System.Windows.Application.Current.MainWindow,
            Width = 1000, Height = 780, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image { Source = source, Stretch = Stretch.None }
            }
        };
        window.ShowDialog();
    }
}
