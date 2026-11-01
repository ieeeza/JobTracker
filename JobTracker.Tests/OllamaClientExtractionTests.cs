using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Application.Interfaces;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infraestructure.Ollama;
using JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobTracker.Tests;

public sealed class OllamaClientExtractionTests
{
    private const string PdfText = "Vaga para desenvolvimento de sistemas e aplicações.";

    [Theory]
    [InlineData("multiple", ImageAnalysisFailure.ConflictingJobs)]
    [InlineData("invalid", ImageAnalysisFailure.InvalidResponse)]
    [InlineData("no-job", ImageAnalysisFailure.InvalidResponse)]
    [InlineData("length", ImageAnalysisFailure.ContextExhausted)]
    [InlineData("http", ImageAnalysisFailure.ServerFailure)]
    public async Task PdfRejectsFailuresWithoutCachingAndReleasesInference(string failure, ImageAnalysisFailure expected)
    {
        using var handler = new ExtractionHandler { Failure = failure };
        using var http = new HttpClient(handler);
        var cache = new MemoryCache();
        using var client = new OllamaClient(http, cache, NullLogger<OllamaClient>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<ImageAnalysisException>(() => client.ExtractPdfAsync(PdfText, new OllamaOptions(), deadline.Token));
        Assert.Equal(expected, error.Failure);
        Assert.Empty(cache.Entries);

        handler.Failure = "";
        var result = await client.ExtractPdfAsync(PdfText, new OllamaOptions(), deadline.Token);
        Assert.Equal(OllamaTestData.Job.Salary, result.Salary);
        Assert.Single(cache.Entries);
    }

    [Fact]
    public async Task CachedPdfIsValidatedBeforeUse()
    {
        using var handler = new ExtractionHandler();
        using var http = new HttpClient(handler);
        var cache = new MemoryCache();
        using var client = new OllamaClient(http, cache, NullLogger<OllamaClient>.Instance);
        await client.ExtractPdfAsync(PdfText, new OllamaOptions(), CancellationToken.None);
        string key = Assert.Single(cache.Entries).Key;
        cache.Entries[key] = JsonSerializer.Serialize(OllamaTestData.Job with { MultipleJobs = true }, OllamaExtractionContract.ExtractionJsonOptions);
        var error = await Assert.ThrowsAsync<ImageAnalysisException>(() => client.ExtractPdfAsync(PdfText, new OllamaOptions(), CancellationToken.None));
        Assert.Equal(ImageAnalysisFailure.ConflictingJobs, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ScreenshotsKeepVisualPromptImagesAndSeparateCacheFromPdf()
    {
        using var handler = new ExtractionHandler();
        using var http = new HttpClient(handler);
        var cache = new MemoryCache();
        using var client = new OllamaClient(http, cache, NullLogger<OllamaClient>.Instance);
        var options = new OllamaOptions();
        string[] images = [Convert.ToBase64String(Encoding.UTF8.GetBytes("test-image"))];
        var screenshot = await client.ExtractAsync(options, images, 0, options.ContextTokens, CancellationToken.None);
        var pdf = await client.ExtractPdfAsync(PdfText, options, CancellationToken.None);
        await client.ExtractAsync(options, images, 0, options.ContextTokens, CancellationToken.None);
        await client.ExtractPdfAsync(PdfText, options, CancellationToken.None);

        Assert.Equal(2, cache.Entries.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(screenshot.Salary, pdf.Salary);
        Assert.Equal(screenshot.Requirements, pdf.Requirements);
        var visual = handler.Requests[0];
        var text = handler.Requests[1];
        Assert.Equal(OllamaExtractionContract.SystemPrompt, visual.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal(images[0], visual.GetProperty("messages")[1].GetProperty("images")[0].GetString());
        Assert.Equal(OllamaPdfExtractionContract.SystemPrompt, text.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.NotEqual(OllamaExtractionContract.SystemPrompt, OllamaPdfExtractionContract.SystemPrompt);
        Assert.Equal(visual.GetProperty("format").GetRawText(), text.GetProperty("format").GetRawText());
        Assert.False(text.GetProperty("messages")[1].TryGetProperty("images", out _));
        Assert.Equal(options.PdfKeepAliveSeconds, text.GetProperty("keep_alive").GetInt32());
        Assert.False(text.GetProperty("think").GetBoolean());
        Assert.False(text.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task CancellationDoesNotWriteCacheOrBlockNextRequest()
    {
        using var handler = new ExtractionHandler();
        using var http = new HttpClient(handler);
        var cache = new MemoryCache();
        using var client = new OllamaClient(http, cache, NullLogger<OllamaClient>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExtractPdfAsync(PdfText, new OllamaOptions(), cancellation.Token));
        Assert.Empty(cache.Entries);
        Assert.Empty(handler.Requests);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ExtractPdfAsync(PdfText, new OllamaOptions(), deadline.Token);
        Assert.Single(cache.Entries);
    }

    private sealed class MemoryCache : IAnalysisCache
    {
        public Dictionary<string, string> Entries { get; } = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct) => Task.FromResult(Entries.GetValueOrDefault(key));
        public Task WriteAsync(string key, string payload, CancellationToken ct)
        {
            Entries[key] = payload;
            return Task.CompletedTask;
        }
    }

    private sealed class ExtractionHandler : HttpMessageHandler
    {
        public string Failure { get; set; } = "";
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return JsonResponse("""{"models":[{"name":"qwen3-vl:4b-instruct","digest":"test-digest"}]}""");
            if (request.RequestUri?.AbsolutePath == "/api/show")
                return JsonResponse("""{"capabilities":["completion","vision"],"model_info":{"general.architecture":"qwen3vl","qwen3vl.context_length":262144}}""");
            Assert.NotNull(request.Content);
            using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            Requests.Add(document.RootElement.Clone());
            if (Failure == "http") return new(HttpStatusCode.BadRequest);
            string content = Failure switch
            {
                "invalid" => "{}",
                "no-job" => JsonSerializer.Serialize(OllamaTestData.Job with
                {
                    Title = null,
                    Description = null,
                    Responsibilities = [],
                    Requirements = [],
                    PreferredRequirements = [],
                    Benefits = []
                }, OllamaExtractionContract.ExtractionJsonOptions),
                "multiple" => JsonSerializer.Serialize(OllamaTestData.Job with { MultipleJobs = true }, OllamaExtractionContract.ExtractionJsonOptions),
                _ => OllamaTestData.Json
            };
            return JsonResponse(JsonSerializer.Serialize(new
            {
                done = true,
                done_reason = Failure == "length" ? "length" : "stop",
                message = new { role = "assistant", content }
            }));
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
