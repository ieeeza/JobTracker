using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using JobTracker.Modules.Settings.Application;

namespace JobTracker.Presentation;

public sealed class DesktopServices(IDatabaseMaintenance database)
{
    public bool Confirm(string title, string message)
    {
        var window = new Window { Title = title, Width = 590, Height = 410, MinWidth = 420, MinHeight = 280,
            Owner = System.Windows.Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)System.Windows.Application.Current.FindResource("SurfaceBrush"),
            Foreground = (Brush)System.Windows.Application.Current.FindResource("TextBrush"), ShowInTaskbar = false };
        var panel = new DockPanel { Margin = new Thickness(24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 100 };
        var accept = new Button { Content = "Confirmar", MinWidth = 100 };
        accept.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(accept);
        DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        panel.Children.Add(new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Content = panel;
        return window.ShowDialog() == true;
    }
    public string? SavePath(string extension)
    {
        string initialDirectory = Path.Combine(Path.GetDirectoryName(database.DatabasePath) ?? throw new IOException("Diretório de dados inválido."), extension == "db" ? "Backups" : "Exports");
        Directory.CreateDirectory(initialDirectory);
        var dialog = new SaveFileDialog { FileName = $"JobTracker-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}",
            InitialDirectory = initialDirectory, DefaultExt = extension, Filter = $"Arquivo {extension.ToUpperInvariant()}|*.{extension}", AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public string? OpenPath(string extension)
    {
        var dialog = new OpenFileDialog { Filter = $"Arquivo {extension.ToUpperInvariant()}|*.{extension}", CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Esta vaga não possui uma URL HTTP/HTTPS válida.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    public void Copy(string text) => Clipboard.SetText(text);
    public void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { Path.GetDirectoryName(path) ?? path }, UseShellExecute = true });
    public void SetTheme(bool dark, bool reduceMotion)
    {
        var resources = System.Windows.Application.Current.Resources;
        (string Key, string Light, string Dark)[] colors = [
            ("BackgroundBrush", "#F3F5F9", "#10151F"), ("SurfaceBrush", "#FFFFFF", "#1A2231"),
            ("TextBrush", "#182335", "#F0F4FA"), ("MutedBrush", "#53637B", "#ADBBD0"),
            ("BorderBrush", "#D5DDE8", "#35435A"), ("AccentBrush", "#3159C8", "#8BAAFF"),
            ("AccentSoftBrush", "#E7EEFF", "#253C67")];
        foreach (var (key, light, night) in colors) resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? night : light));
        if (System.Windows.Application.Current.MainWindow is { } window)
            MotionPreferences.SetEnabled(window, !reduceMotion && SystemParameters.ClientAreaAnimation);
    }
}
