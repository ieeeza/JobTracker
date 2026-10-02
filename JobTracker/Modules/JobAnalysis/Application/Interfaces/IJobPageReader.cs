using JobTracker.Modules.JobAnalysis.Application.Models;

namespace JobTracker.Modules.JobAnalysis.Application.Interfaces
{
    public interface IJobPageReader
    {
        Task<RenderedPage> ReadAsync(
        string url,
        CancellationToken cancellationToken);
    }
}
