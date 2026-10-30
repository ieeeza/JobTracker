using JobTracker.Modules.JobAnalysis.Domain;

namespace JobTracker.Modules.JobAnalysis.Application.Models;

public sealed record PdfJobAnalysisResult(JobContent Content, IReadOnlyList<string> Warnings);
