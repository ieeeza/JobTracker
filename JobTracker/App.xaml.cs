using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Infraestructure.Persistence;
using JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Infrastructure;
using JobTracker.Modules.Settings.Application;
using JobTracker.Modules.Settings.Infrastructure;
using JobTracker.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem;
using JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;

namespace JobTracker;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private Mutex? _instance;
    private bool _ownsMutex;
    private readonly CancellationTokenSource _startupCancellation = new();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        DispatcherUnhandledException += (_, args) =>
        {
            _services?.GetRequiredService<ILogger<App>>().LogCritical("Falha de interface: {Type}; {Stack}", args.Exception.GetType().Name, args.Exception.StackTrace);
            MessageBox.Show("O JobTracker encontrou uma falha inesperada e será encerrado. Reabra o aplicativo; os dados já gravados permanecem no banco local.", "Erro inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        try
        {
            LocalDatabase database = e.Args.Length switch
            {
                0 => LocalDatabase.ForCurrentUser(),
                2 when e.Args[0] == "--database" => new LocalDatabase(e.Args[1]),
                _ => throw new ArgumentException("Argumentos inválidos. Use --database seguido de um caminho absoluto, ou execute sem argumentos.")
            };
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(database.FilePath.ToUpperInvariant())));
            _instance = new Mutex(false, @"Local\JobTracker-" + key);
            try { _ownsMutex = _instance.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
            if (!_ownsMutex) { MessageBox.Show("O JobTracker já está aberto para este banco.", "JobTracker"); Shutdown(); return; }
            var logs = new DiagnosticLoggerProvider(Path.Combine(Path.GetDirectoryName(database.FilePath) ?? throw new IOException("Diretório inválido."), "Logs"));
            ServiceCollection services = new();
            services.AddSingleton(database);
            services.AddDbContextFactory<JobTrackerDbContext>(database.Configure);
            services.AddTransient<PersistenceInitializer>();
            services.AddTransient<ITrackingRepository, TrackingRepository>();
            services.AddTransient<IPreferences, SqlitePreferences>();
            services.AddSingleton<IDatabaseMaintenance, DatabaseMaintenance>();
            services.AddSingleton<JobIdentifier>();
            services.AddSingleton(new ImageLimits());
            services.AddSingleton<ImagePreparationService>();
            services.AddSingleton<IJobImageImporter>(provider => provider.GetRequiredService<ImagePreparationService>());
            services.AddSingleton<System.Net.Http.HttpClient>(_ => new(new System.Net.Http.SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
                ConnectTimeout = TimeSpan.FromSeconds(5), PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }) { Timeout = Timeout.InfiniteTimeSpan });
            services.AddSingleton<IAnalysisCache, AnalysisCache>();
            services.AddSingleton<OllamaClient>();
            services.AddSingleton<IOllamaConnection>(provider => provider.GetRequiredService<OllamaClient>());
            services.AddSingleton<IJobImageAnalyzer, OllamaJobImageAnalyzer>();
            services.AddTransient<JobImageAnalysisService>();
            services.AddTransient<ImageAnalysisSettings>();
            services.AddTransient<OllamaSettingsViewModel>();
            services.AddSingleton<DesktopImageServices>();
            services.AddTransient<ImageImportViewModel>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.IScreenshotDiscovery, JobTracker.Modules.JobAnalysis.Infrastructure.FileSystem.ScreenshotDiscovery>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.IImportBatchStore, BatchImportStore>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.IBatchJobInterpreter, BatchJobInterpreter>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.IBatchGroupingResolver, BatchGroupingResolver>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.ScreenshotGrouping>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchExtraction>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchRegistration>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchImportCoordinator>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchReviewService>();
            services.AddTransient<JobTracker.Modules.JobAnalysis.Application.BatchImport.BatchReportExport>();
            services.AddTransient<BatchImportViewModel>();
            services.AddTransient<TrackingService>();
            services.AddTransient<DataTransferService>();
            services.AddSingleton<ITransferFiles, TransferFiles>();
            services.AddSingleton<DesktopServices>();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning).AddProvider(logs));
            services.AddSingleton(logs);
            services.AddSingleton<ShellViewModel>();
            services.AddTransient<MainWindow>();
            _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            await Task.Run(() => _services.GetRequiredService<PersistenceInitializer>().InitializeAsync(_startupCancellation.Token));
            MainWindow = _services.GetRequiredService<MainWindow>();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            Trace.TraceError("Falha de inicialização: {0}", ex.GetType().Name);
            if (_services is not null)
            {
                _services.GetRequiredService<ILogger<App>>().LogError("Inicialização: {Type}; {Message}; causa: {Inner}; {Stack}",
                    ex.GetType().Name, ex.Message, ex.InnerException?.Message, ex.StackTrace);
                await _services.GetRequiredService<DiagnosticLoggerProvider>().FlushAsync();
            }
            MessageBox.Show("Não foi possível iniciar o JobTracker. Verifique o banco, as permissões e o espaço disponível.\n\n" +
                (ex is ArgumentException ? ex.Message : "Os dados existentes foram preservados. Consulte o log na pasta local."), "Erro de inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _startupCancellation.Cancel(); _startupCancellation.Dispose();
        _services?.Dispose();
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
