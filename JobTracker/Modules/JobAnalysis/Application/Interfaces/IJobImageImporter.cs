using JobTracker.Modules.JobAnalysis.Application.Models;
namespace JobTracker.Modules.JobAnalysis.Application.Interfaces;

public interface IJobImageImporter
{
    Task<JobImageInput> ImportFileAsync(string path, CancellationToken cancellationToken);
    Task<JobImageInput> ImportAsync(string name, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}
