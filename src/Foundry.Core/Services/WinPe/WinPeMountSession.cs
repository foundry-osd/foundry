namespace Foundry.Core.Services.WinPe;

public sealed class WinPeMountSession : IAsyncDisposable
{
    private readonly IWinPeProcessRunner _processRunner;
    private readonly string _dismPath;
    private readonly string _workingDirectory;
    private bool _isMounted;

    private WinPeMountSession(
        IWinPeProcessRunner processRunner,
        string dismPath,
        string bootWimPath,
        string mountDirectoryPath,
        string workingDirectory)
    {
        _processRunner = processRunner;
        _dismPath = dismPath;
        BootWimPath = bootWimPath;
        MountDirectoryPath = mountDirectoryPath;
        _workingDirectory = workingDirectory;
        _isMounted = true;
    }

    public string BootWimPath { get; }
    public string MountDirectoryPath { get; }

    public static async Task<WinPeResult<WinPeMountSession>> MountAsync(
        IWinPeProcessRunner processRunner,
        string dismPath,
        string bootWimPath,
        string mountDirectoryPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(mountDirectoryPath);

        string args = $"/Mount-Image /ImageFile:{WinPeProcessRunner.Quote(bootWimPath)} /Index:1 /MountDir:{WinPeProcessRunner.Quote(mountDirectoryPath)}";
        WinPeProcessExecution mountResult = await processRunner.RunAsync(
            dismPath,
            args,
            workingDirectory,
            cancellationToken).ConfigureAwait(false);

        if (!mountResult.IsSuccess)
        {
            return WinPeResult<WinPeMountSession>.Failure(
                WinPeErrorCodes.WimMountFailed,
                "Failed to mount boot.wim.",
                mountResult.ToDiagnosticText());
        }

        return WinPeResult<WinPeMountSession>.Success(new WinPeMountSession(
            processRunner,
            dismPath,
            bootWimPath,
            mountDirectoryPath,
            workingDirectory));
    }

    public async Task<WinPeResult> CommitAsync(CancellationToken cancellationToken)
    {
        if (!_isMounted)
        {
            return WinPeResult.Success();
        }

        WinPeProcessExecution commitResult = await _processRunner.RunAsync(
            _dismPath,
            $"/Unmount-Image /MountDir:{WinPeProcessRunner.Quote(MountDirectoryPath)} /Commit",
            _workingDirectory,
            cancellationToken).ConfigureAwait(false);

        if (commitResult.IsSuccess)
        {
            _isMounted = false;
            return WinPeResult.Success();
        }

        WinPeProcessExecution discardResult = await _processRunner.RunAsync(
            _dismPath,
            $"/Unmount-Image /MountDir:{WinPeProcessRunner.Quote(MountDirectoryPath)} /Discard",
            _workingDirectory,
            cancellationToken).ConfigureAwait(false);

        _isMounted = false;

        string details = string.Join(
            Environment.NewLine,
            "Commit failed and discard fallback was attempted.",
            "Commit diagnostics:",
            commitResult.ToDiagnosticText(),
            "Discard diagnostics:",
            discardResult.ToDiagnosticText());

        return WinPeResult.Failure(
            WinPeErrorCodes.WimUnmountFailed,
            "Failed to commit mounted boot.wim changes.",
            details);
    }

    public async Task<WinPeResult> DiscardAsync(CancellationToken cancellationToken)
    {
        if (!_isMounted)
        {
            return WinPeResult.Success();
        }

        WinPeProcessExecution discardResult = await _processRunner.RunAsync(
            _dismPath,
            $"/Unmount-Image /MountDir:{WinPeProcessRunner.Quote(MountDirectoryPath)} /Discard",
            _workingDirectory,
            cancellationToken).ConfigureAwait(false);

        _isMounted = false;
        if (discardResult.IsSuccess)
        {
            return WinPeResult.Success();
        }

        return WinPeResult.Failure(
            WinPeErrorCodes.WimUnmountFailed,
            "Failed to discard mounted boot.wim changes.",
            discardResult.ToDiagnosticText());
    }

    public async ValueTask DisposeAsync()
    {
        if (_isMounted)
        {
            await DiscardAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
