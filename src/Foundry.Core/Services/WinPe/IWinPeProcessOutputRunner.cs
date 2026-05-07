namespace Foundry.Core.Services.WinPe;

internal interface IWinPeProcessOutputRunner : IWinPeProcessRunner
{
    Task<WinPeProcessExecution> RunWithOutputAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onOutputData,
        Action<string>? onErrorData,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentOverrides = null);
}
