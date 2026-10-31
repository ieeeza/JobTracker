using System.Text.Json;
using System.Text.Json.Nodes;
using JobTracker.Modules.JobAnalysis.Application.Models;
using JobTracker.Modules.JobAnalysis.Infrastructure.Ollama;
using Xunit;

namespace JobTracker.Tests;

public sealed class OllamaResultMapperTests
{
    [Fact]
    public void PdfAndScreenshotsPreserveTheSameDetailedContent()
    {
        var data = OllamaResultMapper.Parse(OllamaTestData.Json);
        var pdf = OllamaResultMapper.MapPdf(data, CancellationToken.None);
        var screenshots = OllamaResultMapper.Map([data], OllamaOptions.DefaultModel, 2, CancellationToken.None);

        Assert.Equal(screenshots.Content, pdf.Content);
        Assert.Equal(data.Title, pdf.Content.Title);
        Assert.Equal(data.Company, pdf.Content.Company);
        Assert.Equal(data.Seniority, pdf.Content.Seniority);
        Assert.Equal(data.WorkMode, pdf.Content.WorkMode);
        Assert.Equal(data.Location, pdf.Content.Location);
        Assert.Equal(data.EmploymentType, pdf.Content.Contract);
        Assert.Equal(data.Salary, pdf.Content.Salary);
        Assert.Equal(data.Url, pdf.Url);
        Assert.Contains("C#", pdf.Content.Technologies);
        Assert.Contains("SQL", pdf.Content.Technologies);
        Assert.Contains(data.Description ?? "", pdf.Content.Description);
        foreach (string item in data.Responsibilities.Concat(data.Requirements).Concat(data.PreferredRequirements).Concat(data.Benefits))
            Assert.Contains(item, pdf.Content.Description);
        Assert.Contains("Requisitos desejáveis", pdf.Content.Requirements);
        Assert.Contains(data.Requirements[0], pdf.Content.Requirements);
        Assert.Contains(data.PreferredRequirements[0], pdf.Content.Requirements);
        Assert.Contains(pdf.Warnings, warning => warning.Contains("PDF", StringComparison.Ordinal));
        Assert.DoesNotContain(pdf.Warnings, warning => warning.Contains("imagens", StringComparison.Ordinal));
        Assert.Contains(screenshots.Warnings, warning => warning.Contains("imagens", StringComparison.Ordinal));
        Assert.Contains("2 print(s), 1 lote(s)", screenshots.Evidence);
        Assert.Null(screenshots.Capture);
        Assert.Contains("seniority", screenshots.ReviewText);
    }

    [Fact]
    public void RejectsMultipleJobsAndConflictingScreenshotBatches()
    {
        var multiple = OllamaTestData.Job with { MultipleJobs = true };
        var pdfError = Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.MapPdf(multiple, CancellationToken.None));
        Assert.Equal(ImageAnalysisFailure.ConflictingJobs, pdfError.Failure);
        var imageError = Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.Map(
            [OllamaTestData.Job, OllamaTestData.Job with { Company = "Outra empresa" }],
            OllamaOptions.DefaultModel, 2, CancellationToken.None));
        Assert.Equal(ImageAnalysisFailure.ConflictingJobs, imageError.Failure);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("null-list")]
    [InlineData("wrong-type")]
    [InlineData("duplicate")]
    public void RejectsMalformedContracts(string mutation)
    {
        JsonObject json = JsonSerializer.SerializeToNode(OllamaTestData.Job, OllamaExtractionContract.ExtractionJsonOptions)?.AsObject()
            ?? throw new InvalidOperationException("Missing test data.");
        switch (mutation)
        {
            case "missing": json.Remove("salary"); break;
            case "extra": json["unknown"] = "value"; break;
            case "null-list": json["requirements"] = null; break;
            case "wrong-type": json["multipleJobs"] = "false"; break;
        }
        string payload = mutation == "duplicate"
            ? json.ToJsonString().Insert(1, "\"multipleJobs\":true,")
            : json.ToJsonString();
        Assert.Throws<JsonException>(() => OllamaResultMapper.Parse(payload));
    }

    [Fact]
    public void RejectsInvalidUrlsAndLimits()
    {
        JsonNode? nullItem = JsonNode.Parse(OllamaTestData.Json);
        Assert.NotNull(nullItem);
        nullItem["requirements"] = new JsonArray((JsonNode?)null);
        Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.Parse(nullItem.ToJsonString()));
        Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.Validate(OllamaTestData.Job with { Url = "file:///secret" }));
        Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.Validate(OllamaTestData.Job with { Salary = new string('a', 501) }));
        Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.Validate(OllamaTestData.Job with { Requirements = [new string('a', 2001)] }));
        Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.Validate(OllamaTestData.Job with { Technologies = Enumerable.Repeat("C#", 101).ToArray() }));
    }

    [Fact]
    public void RejectsPdfWithoutIdentifiableJob()
    {
        var data = OllamaTestData.Job with
        {
            Title = null,
            Description = null,
            Responsibilities = [],
            Requirements = [],
            PreferredRequirements = [],
            Benefits = []
        };
        Assert.Throws<ImageAnalysisException>(() => OllamaResultMapper.MapPdf(data, CancellationToken.None));
    }

    [Fact]
    public void PdfMappingHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => OllamaResultMapper.MapPdf(OllamaTestData.Job, cancellation.Token));
    }
}
