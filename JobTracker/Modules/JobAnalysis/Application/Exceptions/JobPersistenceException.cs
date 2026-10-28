namespace JobTracker.Modules.JobAnalysis.Application.Exceptions;

public sealed class JobPersistenceException(string message, Exception innerException)
    : Exception(message, innerException);
