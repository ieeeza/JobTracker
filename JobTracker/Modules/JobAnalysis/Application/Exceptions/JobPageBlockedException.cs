namespace JobTracker.Modules.JobAnalysis.Application.Exceptions
{
    public class JobPageBlockedException(string url) : Exception($"O site bloqueou a leitura automatizada: {url}")
    {
    }
}
