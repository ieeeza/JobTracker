using System.IO;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Application.Services;
using JobTracker.Modules.JobAnalysis.Domain;
using JobTracker.Modules.JobAnalysis.Infraestructure.Persistence;
using JobTracker.Modules.JobAnalysis.Infrastructure.Persistence;
using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;
using JobTracker.Modules.JobApplications.Infrastructure;
using JobTracker.Modules.Settings.Infrastructure;
using JobTracker.Presentation.Services;
using JobTracker.Presentation.ViewModels.Imports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobTracker.Tests;

public sealed class PdfImportSaveTests
{
    private static readonly JobContent Content = new("Desenvolvedor", "Empresa", "Descrição e benefícios",
        "São Paulo", "Remoto", "Pleno", "CLT", "R$ 8.000", "Requisitos", "Observações", "PDF",
        "Revisar candidatura", new DateTime(2026, 11, 2), "C#, SQL");

    [Fact]
    public async Task SavesReviewedFieldsAndRefreshesWorkspaceWithoutApplyingOrSavingTwice()
    {
        using var fixture = new Fixture();
        await fixture.AnalyzeAsync();
        Assert.Empty(await fixture.Tracking.ListAsync(default));
        fixture.ViewModel.Draft.Title = "Cargo corrigido";
        fixture.ViewModel.Draft.Salary = "R$ 9.000";
        var reviewed = fixture.ViewModel.Draft.Content;

        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);

        var saved = Assert.Single(await fixture.Tracking.ListAsync(default));
        Assert.Equal(reviewed, saved.Content);
        Assert.Equal(ApplicationStatus.Saved, saved.Status);
        Assert.Null(saved.AppliedAt);
        Assert.Equal("PDF (IA local)", saved.Source);
        Assert.Null(saved.OcrCapture);
        Assert.Equal(saved.Id, Assert.Single(fixture.Workspace.All).Id);
        Assert.True(fixture.ViewModel.IsSaved);
        Assert.False(fixture.ViewModel.CanEditDraft);
        Assert.False(fixture.ViewModel.SavePdfCommand.CanExecute(null));
        Assert.Equal("Texto original", fixture.ViewModel.ExtractedText);
    }

    [Theory]
    [InlineData("", "Empresa", "", "cargo")]
    [InlineData("Cargo", " ", "", "empresa")]
    [InlineData("Cargo", "Empresa", "file:///C:/vaga.pdf", "HTTP")]
    public async Task InvalidInputPreservesDraftAndCanBeCorrected(string title, string company, string url, string error)
    {
        using var fixture = new Fixture();
        await fixture.AnalyzeAsync();
        fixture.ViewModel.Draft.Title = title;
        fixture.ViewModel.Draft.Company = company;
        fixture.ViewModel.Draft.Url = url;

        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);

        Assert.Empty(await fixture.Tracking.ListAsync(default));
        Assert.Contains(error, fixture.ViewModel.StatusMessage);
        Assert.Equal(title, fixture.ViewModel.Draft.Title);
        Assert.Equal(company, fixture.ViewModel.Draft.Company);
        Assert.Equal(url, fixture.ViewModel.Draft.Url);
        Assert.True(fixture.ViewModel.CanEditDraft);
        Assert.False(fixture.ViewModel.IsSaved);
        fixture.ViewModel.Draft.Fill(Content);
        fixture.ViewModel.Draft.Url = "";
        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        Assert.Single(await fixture.Tracking.ListAsync(default));
    }

    [Fact]
    public async Task RejectsFieldsAboveDomainLimitsWithoutTruncation()
    {
        using var fixture = new Fixture();
        await fixture.AnalyzeAsync();
        fixture.ViewModel.Draft.Title = new string('x', 301);
        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        Assert.Empty(await fixture.Tracking.ListAsync(default));
        Assert.Contains("300", fixture.ViewModel.StatusMessage);
        Assert.Equal(301, fixture.ViewModel.Draft.Title.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsDuplicateUrlIncludingDeletedJobs(bool deleted)
    {
        using var fixture = new Fixture();
        await fixture.AnalyzeAsync();
        const string url = "https://example.com/jobs/1";
        await fixture.Tracking.CreateAsync(url, Content, null, "Manual", ApplicationStatus.Saved, null, false, default);
        if (deleted)
            await fixture.Tracking.DeleteAsync(Assert.Single(await fixture.Tracking.ListAsync(default)), true, default);
        fixture.ViewModel.Draft.Url = url + "?utm_source=pdf";

        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);

        Assert.Single(await fixture.Tracking.ListAsync(default));
        Assert.Contains("URL já está cadastrada", fixture.ViewModel.StatusMessage);
        Assert.False(fixture.ViewModel.IsSaved);
    }

    [Fact]
    public async Task RefreshFailureAfterCommitDoesNotAllowAnotherInsert()
    {
        using var fixture = new Fixture();
        await fixture.AnalyzeAsync();
        fixture.Workspace.Reloaded += (_, _) => throw new InvalidOperationException("Falha de atualização simulada");

        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);

        Assert.Single(await fixture.Tracking.ListAsync(default));
        Assert.True(fixture.ViewModel.IsSaved);
        Assert.Contains("Vaga salva no banco", fixture.ViewModel.StatusMessage);
        Assert.False(fixture.ViewModel.SavePdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task DatabaseFailurePreservesDraftForRetryAndDoesNotExposeDatabaseDetails()
    {
        using var fixture = new Fixture();
        await fixture.AnalyzeAsync();
        fixture.ExecuteSql("CREATE TRIGGER FailPdfInsert BEFORE INSERT ON Jobs BEGIN SELECT RAISE(ABORT, 'sensitive-test-diagnostic'); END;");

        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);

        Assert.Empty(await fixture.Tracking.ListAsync(default));
        Assert.False(fixture.ViewModel.IsSaved);
        Assert.Equal(Content, fixture.ViewModel.Draft.Content);
        Assert.Contains("Falha ao salvar", fixture.ViewModel.StatusMessage);
        Assert.DoesNotContain("sensitive-test-diagnostic", fixture.ViewModel.StatusMessage);
        fixture.ExecuteSql("DROP TRIGGER FailPdfInsert;");
        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        Assert.Single(await fixture.Tracking.ListAsync(default));
    }

    [Fact]
    public async Task ConcurrentSaveAttemptsCreateOnlyOneJob()
    {
        using var fixture = new Fixture();
        await fixture.AnalyzeAsync();
        Task first = fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        Task second = fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        await Task.WhenAll(first, second);
        Assert.Single(await fixture.Tracking.ListAsync(default));
    }

    [Fact]
    public async Task RequiresAnalysisAndBlocksSavingDuringOtherOperations()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        Assert.False(fixture.ViewModel.SavePdfCommand.CanExecute(null));
        await fixture.AnalyzeAsync();
        await fixture.Operations.RunAsync("Outra operação", async _ =>
        {
            Assert.False(fixture.ViewModel.SavePdfCommand.CanExecute(null));
            Assert.False(fixture.ViewModel.CanEditDraft);
            await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        });
        Assert.Empty(await fixture.Tracking.ListAsync(default));
        Assert.True(fixture.ViewModel.SavePdfCommand.CanExecute(null));

        await fixture.ViewModel.SavePdfCommand.ExecuteAsync(null);
        fixture.ViewModel.SelectPdfCommand.Execute(null);
        Assert.False(fixture.ViewModel.HasAnalysis);
        Assert.False(fixture.ViewModel.IsSaved);
        Assert.False(fixture.ViewModel.SavePdfCommand.CanExecute(null));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public TrackingService Tracking { get; }
        public JobWorkspace Workspace { get; }
        public UiOperationState Operations { get; } = new(NullLogger<UiOperationState>.Instance);
        public PdfImportViewModel ViewModel { get; }

        public Fixture()
        {
            _connection.Open();
            var options = new DbContextOptionsBuilder<JobTrackerDbContext>().UseSqlite(_connection).Options;
            var factory = new PooledDbContextFactory<JobTrackerDbContext>(options);
            using (var db = factory.CreateDbContext()) db.Database.EnsureCreated();
            Tracking = new TrackingService(new TrackingRepository(factory), new JobIdentifier());
            Workspace = new JobWorkspace(Tracking);
            var desktop = new DesktopServices(new DatabaseMaintenance(new LocalDatabase(Path.Combine(Path.GetTempPath(), "unused-pdf-test.db"))));
            ViewModel = new PdfImportViewModel(new FilePicker(), new TextExtractor(), new Analyzer(), Operations,
                NullLogger<PdfImportViewModel>.Instance, Tracking, Workspace, desktop);
        }

        public async Task AnalyzeAsync()
        {
            ViewModel.SelectPdfCommand.Execute(null);
            await ViewModel.ExtractPdfCommand.ExecuteAsync(null);
            await ViewModel.AnalyzePdfCommand.ExecuteAsync(null);
            Assert.True(ViewModel.HasAnalysis);
        }

        public void ExecuteSql(string sql)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            Operations.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class FilePicker : IFilePickerService
    {
        public string selectPdf() => "vaga.pdf";
    }

    private sealed class TextExtractor : IPdfTextExtractor
    {
        public string Extract(string filepath, CancellationToken cancellationToken = default) => "Texto original";
    }

    private sealed class Analyzer : IJobPdfAnalyzer
    {
        public Task<PdfJobAnalysisResult> AnalyzeAsync(string extractedText, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PdfJobAnalysisResult(Content, ["Confira a remuneração."]));
    }
}
