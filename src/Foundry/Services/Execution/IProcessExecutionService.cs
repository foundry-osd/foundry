namespace Foundry.Services.Execution;

public interface IProcessExecutionService
{
    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}
