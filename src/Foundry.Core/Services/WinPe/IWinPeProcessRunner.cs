namespace Foundry.Core.Services.WinPe;

public interface IWinPeProcessRunner
{
    Task<WinPeProcessExecution> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null);

    Task<WinPeProcessExecution> RunCmdScriptAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken);

    Task<WinPeProcessExecution> RunCmdScriptDirectAsync(
        string scriptPath,
        string scriptArguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}
