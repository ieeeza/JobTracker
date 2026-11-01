namespace JobTracker.Modules.JobAnalysis.Application.Models;

public sealed record PdfAnalysisEstimate(int Characters, int Utf8Bytes, long TextTokens,
    long RequiredTokens, int ContextTokens, int MaximumContextTokens, int BlockCount);

public sealed record PdfAnalysisProgress(int Block, int BlockCount, int ContextTokens);
