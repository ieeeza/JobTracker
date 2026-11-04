using JobTracker.Modules.JobAnalysis.Application.BatchImport;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Infraestructure;
using JobTracker.Modules.JobAnalysis.Infraestructure.Persistence;
using JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;
using JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;
using JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Infrastructure;
using JobTracker.Modules.Settings.Application;
using JobTracker.Modules.Settings.Infrastructure;
using JobTracker.Presentation;
using JobTracker.Presentation.Services;
using JobTracker.Presentation.ViewModels;
using JobTracker.Presentation.ViewModels.Imports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace JobTracker;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _startupCancellation = new();
    private ServiceProvider? _services;
    private Mutex? _instance;
    private bool _ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureCulture();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            LocalDatabase database = CreateDatabase(e.Args);

            if (!TryAcquireInstance(database))
            {
                MessageBox.Show("O JobTracker já está aberto para este banco.", "JobTracker");
                Shutdown();
                return;
            }

            _services = BuildServices(database);

            await Task.Run(() => _services.GetRequiredService<PersistenceInitializer>()
                .InitializeAsync(_startupCancellation.Token));

            ShowMainWindow(_services);
        }
        catch (Exception ex)
        {
            await HandleStartupFailureAsync(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _startupCancellation.Cancel();
        _startupCancellation.Dispose();
        _services?.Dispose();

        if (_ownsMutex)
        {
            _instance?.ReleaseMutex();
        }

        _instance?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureCulture()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("pt-BR");

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static LocalDatabase CreateDatabase(string[] args)
    {
        return args.Length switch
        {
            0 => LocalDatabase.ForCurrentUser(),
            2 when args[0] == "--database" => new LocalDatabase(args[1]),
            _ => throw new ArgumentException(
                "Argumentos inválidos. Use --database seguido de um caminho absoluto, ou execute sem argumentos.")
        };
    }

    private bool TryAcquireInstance(LocalDatabase database)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(database.FilePath.ToUpperInvariant());
        string key = Convert.ToHexString(SHA256.HashData(pathBytes));
        _instance = new Mutex(false, @"Local\JobTracker-" + key);

        try
        {
            _ownsMutex = _instance.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // WaitOne concede a posse quando a instância anterior abandona o mutex.
            _ownsMutex = true;
        }

        return _ownsMutex;
    }

    private static ServiceProvider BuildServices(LocalDatabase database)
    {
        string databaseDirectory = Path.GetDirectoryName(database.FilePath)
            ?? throw new IOException("Diretório inválido.");
        var logs = new DiagnosticLoggerProvider(Path.Combine(databaseDirectory, "Logs"));
        ServiceCollection services = new();

        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddFilter("JobTracker.Modules.JobAnalysis.Infrastructure.Ollama.OllamaClient", LogLevel.Information)
            .AddProvider(logs));
        services.AddSingleton(logs);

        RegisterPersistence(services, database);
        RegisterApplicationServices(services);
        RegisterImageAnalysis(services);
        RegisterBatchImport(services);
        RegisterPresentation(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    private static void RegisterPersistence(IServiceCollection services, LocalDatabase database)
    {
        services.AddSingleton(database);
        services.AddDbContextFactory<JobTrackerDbContext>(database.Configure);
        services.AddTransient<PersistenceInitializer>();
        services.AddTransient<ITrackingRepository, TrackingRepository>();
        services.AddTransient<IPreferences, SqlitePreferences>();
        services.AddSingleton<IDatabaseMaintenance, DatabaseMaintenance>();
    }

    private static void RegisterApplicationServices(IServiceCollection services)
    {
        services.AddSingleton<JobIdentifier>();
        services.AddTransient<TrackingService>();
        services.AddTransient<DataTransferService>();
        services.AddSingleton<ITransferFiles, TransferFiles>();
    }

    private static void RegisterImageAnalysis(IServiceCollection services)
    {
        services.AddSingleton(new ImageLimits());
        services.AddSingleton<ImagePreparationService>();
        services.AddSingleton<IJobImageImporter>(provider =>
            provider.GetRequiredService<ImagePreparationService>());

        services.AddSingleton<HttpClient>(_ => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = Timeout.InfiniteTimeSpan
        });

        services.AddSingleton<IAnalysisCache, AnalysisCache>();
        services.AddSingleton<OllamaClient>();
        services.AddSingleton<IOllamaConnection>(provider =>
            provider.GetRequiredService<OllamaClient>());
        services.AddSingleton<IJobImageAnalyzer, OllamaJobImageAnalyzer>();
        services.AddTransient<JobImageAnalysisService>();
        services.AddTransient<IJobPdfAnalyzer, OllamaJobPdfAnalyzer>();
        services.AddTransient<ImageAnalysisSettings>();
    }

    private static void RegisterBatchImport(IServiceCollection services)
    {
        services.AddTransient<IScreenshotDiscovery, ScreenshotDiscovery>();
        services.AddTransient<IImportBatchStore, BatchImportStore>();
        services.AddTransient<IBatchJobInterpreter, BatchJobInterpreter>();
        services.AddTransient<IBatchGroupingResolver, BatchGroupingResolver>();
        services.AddTransient<ScreenshotGrouping>();
        services.AddTransient<BatchExtraction>();
        services.AddTransient<BatchRegistration>();
        services.AddTransient<BatchImportCoordinator>();
        services.AddTransient<BatchReviewService>();
        services.AddTransient<BatchReportExport>();
    }

    private static void RegisterPresentation(IServiceCollection services)
    {
        services.AddSingleton<DesktopServices>();
        services.AddSingleton<DesktopImageServices>();
        services.AddSingleton<UiOperationState>();
        services.AddSingleton<JobWorkspace>();

        services.AddSingleton<OllamaSettingsViewModel>();
        services.AddSingleton<ImageImportViewModel>();
        services.AddSingleton<BatchImportViewModel>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<JobsViewModel>();
        services.AddSingleton<BoardViewModel>();
        services.AddSingleton<AgendaViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<JobEditorViewModel>();
        services.AddSingleton<ShellViewModel>();

        services.AddSingleton<IFilePickerService, PdfFilePickerService>();

        services.AddTransient<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddTransient<PdfImportViewModel>();
        services.AddTransient<MainWindow>();
    }

    private void ShowMainWindow(IServiceProvider services)
    {
        MainWindow = services.GetRequiredService<MainWindow>();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _services?.GetRequiredService<ILogger<App>>().LogCritical(
            "Falha de interface: {Type}; {Stack}",
            e.Exception.GetType().Name,
            e.Exception.StackTrace);

        MessageBox.Show(
            "O JobTracker encontrou uma falha inesperada e será encerrado. " +
            "Reabra o aplicativo; os dados já gravados permanecem no banco local.",
            "Erro inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(1);
    }

    private async Task HandleStartupFailureAsync(Exception exception)
    {
        Trace.TraceError("Falha de inicialização: {0}", exception.GetType().Name);

        if (_services is not null)
        {
            _services.GetRequiredService<ILogger<App>>().LogError(
                "Inicialização: {Type}; {Message}; causa: {Inner}; {Stack}",
                exception.GetType().Name,
                exception.Message,
                exception.InnerException?.Message,
                exception.StackTrace);

            await _services.GetRequiredService<DiagnosticLoggerProvider>().FlushAsync();
        }

        string details = exception is ArgumentException
            ? exception.Message
            : "Os dados existentes foram preservados. Consulte o log na pasta local.";

        MessageBox.Show(
            "Não foi possível iniciar o JobTracker. Verifique o banco, as permissões e o espaço disponível.\n\n" + details,
            "Erro de inicialização",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown(1);
    }
}
