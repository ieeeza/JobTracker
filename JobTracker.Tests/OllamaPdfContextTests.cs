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

public sealed class OllamaPdfContextTests
{
    [Fact]
    public void SmallDocumentKeepsInitialContext()
    {
        var options = new OllamaOptions { ContextTokens = 12000 };
        Assert.Equal(12000, OllamaPdfContextBudget.SelectContextTokens(new string('a', 100), options));
    }

    [Theory]
    [InlineData('a', 18000)]
    [InlineData('á', 18000)]
    [InlineData('a', 60000)]
    public void ExpandsWithinMaximumIncludingAboveInitialSettingLimit(char letter, int length)
    {
        var options = new OllamaOptions { ContextTokens = 12000, MaxContextTokens = 65536 };
        int selected = OllamaPdfContextBudget.SelectContextTokens(new string(letter, length), options);
        Assert.InRange(selected, 12001, options.MaxContextTokens);
        Assert.Equal(12000, options.ContextTokens);
    }

    [Fact]
    public void AcceptsExactMaximumAndRejectsOneAdditionalByte()
    {
        string text = new('á', 6000);
        int required = checked((int)OllamaPdfContextBudget.Estimate(text).RequiredTokens);
        var options = new OllamaOptions { MaxContextTokens = required };
        Assert.Equal(required, OllamaPdfContextBudget.SelectContextTokens(text, options));
        var error = Assert.Throws<ImageAnalysisException>(() =>
            OllamaPdfContextBudget.SelectContextTokens(text + "a", options));
        Assert.Equal(ImageAnalysisFailure.ContextExhausted, error.Failure);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012345678901234567890")]
    public void RejectsInsufficientText(string text) =>
        Assert.Throws<ArgumentException>(() =>
            OllamaPdfContextBudget.SelectContextTokens(text, new OllamaOptions()));

    [Fact]
    public async Task SendsFullTextWithExpandedContextAndCachesByEffectiveContext()
    {
        string text = new('á', 30000);
        var options = new OllamaOptions { PdfContextTokens = 12000, PdfMaxContextTokens = 65536 };
        using var handler = new PdfHandler();
        using var http = new HttpClient(handler);
        var cache = new MemoryCache();
        using var client = new OllamaClient(http, cache, NullLogger<OllamaClient>.Instance);

        await client.ExtractPdfAsync(text, options, CancellationToken.None);
        await client.ExtractPdfAsync(text, options, CancellationToken.None);

        Assert.Equal(1, handler.ChatCalls);
        Assert.Equal(OllamaPdfExtractionContract.UserPrefix + text, handler.UserContent);
        Assert.InRange(handler.ContextTokens, 32769, options.PdfMaxContextTokens);

        // Changing the initial setting without changing the effective context preserves the cache hit.
        await client.ExtractPdfAsync(text, options with { PdfContextTokens = 16000 }, CancellationToken.None);
        Assert.Equal(1, handler.ChatCalls);
        Assert.Single(cache.Entries);
    }

    [Fact]
    public async Task RejectsOversizedDocumentBeforeHttpOrCache()
    {
        using var handler = new PdfHandler();
        using var http = new HttpClient(handler);
        var cache = new MemoryCache();
        using var client = new OllamaClient(http, cache, NullLogger<OllamaClient>.Instance);
        var error = await Assert.ThrowsAsync<ImageAnalysisException>(() =>
            client.ExtractPdfAsync(new string('a', 20000), new OllamaOptions(), CancellationToken.None));
        Assert.Equal(ImageAnalysisFailure.ContextExhausted, error.Failure);
        Assert.Equal(0, handler.TotalCalls);
        Assert.Empty(cache.Entries);
    }

    private sealed class MemoryCache : IAnalysisCache
    {
        public Dictionary<string, string> Entries { get; } = [];
        public Task<string?> ReadAsync(string key, CancellationToken ct) =>
            Task.FromResult(Entries.GetValueOrDefault(key));
        public Task WriteAsync(string key, string payload, CancellationToken ct)
        {
            Entries[key] = payload;
            return Task.CompletedTask;
        }
    }

    private sealed class PdfHandler : HttpMessageHandler
    {
        public int TotalCalls { get; private set; }
        public int ChatCalls { get; private set; }
        public int ContextTokens { get; private set; }
        public string? UserContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            TotalCalls++;
            if (request.Method == HttpMethod.Get)
                return JsonResponse("""{"models":[{"name":"qwen3-vl:4b-instruct","digest":"test-digest"}]}""");
            if (request.RequestUri?.AbsolutePath == "/api/show")
                return JsonResponse("""{"capabilities":["completion","vision"]}""");

            ChatCalls++;
            Assert.NotNull(request.Content);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            Assert.Equal("qwen3-vl:4b-instruct", body.RootElement.GetProperty("model").GetString());
            Assert.Equal(OllamaExtractionContract.Schema.ToJsonString(), body.RootElement.GetProperty("format").GetRawText());
            var messages = body.RootElement.GetProperty("messages");
            Assert.Equal(OllamaPdfExtractionContract.SystemPrompt, messages[0].GetProperty("content").GetString());
            foreach (var message in messages.EnumerateArray()) Assert.False(message.TryGetProperty("images", out _));
            ContextTokens = body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32();
            UserContent = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
            return JsonResponse(JsonSerializer.Serialize(new
            {
                done = true,
                done_reason = "stop",
                message = new
                {
                    role = "assistant",
                    content = OllamaTestData.Json
                }
            }));
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
