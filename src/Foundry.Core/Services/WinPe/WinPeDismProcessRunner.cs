namespace Foundry.Core.Services.WinPe;

internal static class WinPeDismProcessRunner
{
    public static Task<WinPeProcessExecution> RunAsync(
        IWinPeProcessRunner processRunner,
        string dismPath,
        string arguments,
        string workingDirectory,
        string progressStatus,
        IProgress<WinPeDismProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (progress is not null && processRunner is IWinPeProcessOutputRunner outputRunner)
        {
            var reporter = new WinPeDismProgressReporter(progressStatus, progress);
            return outputRunner.RunWithOutputAsync(
                dismPath,
                arguments,
                workingDirectory,
                reporter.HandleOutput,
                reporter.HandleOutput,
                cancellationToken);
        }

        return processRunner.RunAsync(
            dismPath,
            arguments,
            workingDirectory,
            cancellationToken);
    }
}
