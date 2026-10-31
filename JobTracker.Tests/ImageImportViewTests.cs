using JobTracker.Presentation.Views.Imports;
using JobTracker.Presentation.ViewModels.Imports;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;
using JobTracker.Presentation;
using JobTracker.Presentation.ViewModels;
using JobTracker.Presentation.Services;
using Xunit;

namespace JobTracker.Tests;

public sealed class ImageImportViewTests
{
    [Fact]
    public void ImageImportCanBeOpenedRepeatedlyWithReadOnlyOcrState()
    {
        RunOnSta(() =>
        {
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/JobTracker;component/Presentation/Resources/DesignSystem.xaml", UriKind.Relative)
            });
            try
            {
                VerifyRepeatedOpening(false);
                VerifyRepeatedOpening(true);
            }
            finally { application.Shutdown(); }
        });
    }

    private static void VerifyRepeatedOpening(bool hasCapture)
    {
        var options = new ImageLimits();
        var preprocessing = new ImagePreparationService(options);
        using var http = new System.Net.Http.HttpClient();
        var dbOptions = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<JobTracker.Modules.JobAnalysis.Infrastructure.Persistence.JobTrackerDbContext>();
        var factory = new Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory<JobTracker.Modules.JobAnalysis.Infrastructure.Persistence.JobTrackerDbContext>(dbOptions.Options);
        using var client = new JobTracker.Modules.JobAnalysis.Infrastructure.Ollama.OllamaClient(http,
            new JobTracker.Modules.JobAnalysis.Infrastructure.Persistence.AnalysisCache(factory), Microsoft.Extensions.Logging.Abstractions.NullLogger<JobTracker.Modules.JobAnalysis.Infrastructure.Ollama.OllamaClient>.Instance);
        using var analyzer = new JobTracker.Modules.JobAnalysis.Infrastructure.Ollama.OllamaJobImageAnalyzer(client, preprocessing, options);
        var images = new ImageImportViewModel(preprocessing,
            new JobImageAnalysisService(analyzer),
            options, new DesktopImageServices(),
            new UiOperationState(Microsoft.Extensions.Logging.Abstractions.NullLogger<UiOperationState>.Instance));
        OcrCapture? capture = hasCapture
            ? new(DateTime.UtcNow, "Texto reconhecido", "Manual", "123",
                [new("print.png", "Texto reconhecido", 0.9f, false, null)])
            : null;
        images.Reset(capture);

        // Tab content is materialized when the control is attached to a presentation source.
        var host = new Window { Width = 1100, Height = 900, ShowActivated = false, ShowInTaskbar = false, Left = -10000 };
        host.Show();
        try
        {
            for (int opening = 0; opening < 3; opening++)
            {
                var view = new ImageImportView { DataContext = new ImageContext(images) };
                host.Content = view;
                host.Measure(new Size(1100, 900));
                host.Arrange(new Rect(0, 0, 1100, 900));
                host.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var progress = Assert.Single(Descendants<ProgressBar>(view));
                Assert.Equal(images.Progress, progress.Value);
                Assert.False(progress.GetBindingExpression(ProgressBar.ValueProperty)?.HasError ?? true);
                Assert.Equal(hasCapture, images.HasCapture);
                if (hasCapture)
                {
                    Assert.Contains(Descendants<TextBox>(view), box => box.Text.Contains("Texto reconhecido", StringComparison.Ordinal) && box.IsReadOnly);
                }
                host.Content = null;
                host.UpdateLayout();
            }
        }
        finally { host.Close(); }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "O teste WPF excedeu 30 segundos.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public sealed record ImageContext(ImageImportViewModel Images)
    {
        public int ScreenshotModeIndex { get; set; }
    }
}
