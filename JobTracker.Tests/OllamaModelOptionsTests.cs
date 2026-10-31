using System.Text.Json;
using JobTracker.Modules.JobAnalysis.Application.Models;
using Xunit;

namespace JobTracker.Tests;

public sealed class OllamaModelOptionsTests
{
    [Fact]
    public void NewOptionsUseFourBInstruct()
    {
        var options = new OllamaOptions();
        Assert.Equal("qwen3-vl:4b-instruct", options.Model);
        options.Validate();
    }

    [Theory]
    [InlineData("{}", "qwen3-vl:4b-instruct")]
    [InlineData("{\"Model\":\"qwen3-vl:8b\"}", "qwen3-vl:8b")]
    [InlineData("{\"Model\":\"qwen3-vl:4b-instruct\"}", "qwen3-vl:4b-instruct")]
    public void SavedOptionsPreserveExplicitModelAndDefaultMissingModel(string json, string expected)
    {
        var options = JsonSerializer.Deserialize<OllamaOptions>(json);
        Assert.NotNull(options);
        Assert.Equal(expected, options.Model);
        options.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("qwen3-vl:4b-instruct-cloud")]
    [InlineData("invalid model")]
    public void RejectsUnsupportedModels(string model) =>
        Assert.Throws<ArgumentException>(() => new OllamaOptions { Model = model }.Validate());
}
