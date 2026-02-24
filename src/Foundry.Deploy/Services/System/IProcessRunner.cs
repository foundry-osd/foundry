namespace Foundry.Deploy.Services.System;

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
