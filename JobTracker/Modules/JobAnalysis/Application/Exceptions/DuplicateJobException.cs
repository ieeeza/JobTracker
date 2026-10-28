namespace JobTracker.Modules.JobAnalysis.Application.Exceptions;

public sealed class DuplicateJobException(Exception innerException)
    : Exception("Esta candidatura já está registrada.", innerException);
