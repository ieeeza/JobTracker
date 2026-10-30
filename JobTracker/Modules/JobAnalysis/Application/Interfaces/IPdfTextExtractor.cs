namespace JobTracker.Modules.JobAnalysis.Application.Interfaces
{
    public interface IPdfTextExtractor
    {
        string Extract(string filepath, CancellationToken cancellationToken = default);
    }
}
