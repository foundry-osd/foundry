// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeMountSession : IAsyncDisposable
{
    private readonly IWinPeProcessRunner _processRunner;
    private readonly string _dismPath;
    private readonly string _workingDirectory;
    private bool _nativeTerminationConfirmed = true;

    private WinPeMountSession(IWinPeProcessRunner processRunner, string dismPath, string bootWimPath,
        string mountDirectoryPath, string workingDirectory)
    {
        _processRunner = processRunner;
        _dismPath = dismPath;
        _workingDirectory = Path.GetFullPath(workingDirectory);
        BootWimPath = Path.GetFullPath(bootWimPath, _workingDirectory);
        MountDirectoryPath = Path.GetFullPath(mountDirectoryPath, _workingDirectory);
    }

    public string BootWimPath { get; }
    public string MountDirectoryPath { get; }
    public WinPeMountState State { get; private set; } = WinPeMountState.Mounting;
    public bool CanDeleteMountDirectory => State == WinPeMountState.Unmounted;
    public WinPeDiagnostic? CleanupDiagnostic { get; private set; }

    public static async Task<WinPeResult<WinPeMountSession>> MountAsync(IWinPeProcessRunner processRunner,
        string dismPath, string bootWimPath, string mountDirectoryPath, string workingDirectory,
        CancellationToken cancellationToken, IProgress<WinPeDismProgress>? dismProgress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = new WinPeMountSession(processRunner, dismPath, bootWimPath, mountDirectoryPath, workingDirectory);
        Directory.CreateDirectory(session.MountDirectoryPath);
        WinPeDiagnostic primary;
        try
        {
            WinPeProcessExecution execution = await WinPeDismProcessRunner.RunAsync(processRunner, dismPath,
                ["/Mount-Image", $"/ImageFile:{session.BootWimPath}", "/Index:1", $"/MountDir:{session.MountDirectoryPath}"],
                session._workingDirectory, "Mounting image with DISM.", dismProgress, cancellationToken).ConfigureAwait(false);
            if (execution.IsSuccess)
            {
                session.State = WinPeMountState.Mounted;
                return WinPeResult<WinPeMountSession>.Success(session);
            }
            primary = execution.ToFailureDiagnostic(WinPeErrorCodes.WimMountFailed, "Failed to mount boot.wim.");
        }
        catch (Exception ex)
        {
            session._nativeTerminationConfirmed = WinPeMountRecovery.IsTerminationConfirmed(ex);
            primary = new WinPeDiagnostic(WinPeErrorCodes.WimMountFailed, "Failed to mount boot.wim.", exception: ex);
        }
        session.State = WinPeMountState.RecoveryRequired;
        using var cleanup = new CancellationTokenSource(WinPeMountRecovery.CleanupTimeout);
        WinPeResult recovery = await session.ReconcileAsync(cleanup.Token).ConfigureAwait(false);
        return WinPeResult<WinPeMountSession>.Failure(WinPeMountRecovery.Combine(primary, recovery));
    }

    public async Task<WinPeResult> CommitAsync(CancellationToken cancellationToken,
        IProgress<WinPeDismProgress>? dismProgress = null)
    {
        if (State == WinPeMountState.Unmounted) return WinPeResult.Success();
        if (State != WinPeMountState.Mounted)
            return WinPeResult.Failure(Ownership(new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "The image requires recovery before further servicing.")));
        WinPeDiagnostic primary;
        try
        {
            WinPeProcessExecution execution = await WinPeDismProcessRunner.RunAsync(_processRunner, _dismPath,
                ["/Unmount-Image", $"/MountDir:{MountDirectoryPath}", "/Commit"], _workingDirectory,
                "Committing image changes with DISM.", dismProgress, cancellationToken).ConfigureAwait(false);
            if (execution.IsSuccess)
            {
                State = WinPeMountState.Unmounted;
                return WinPeResult.Success();
            }
            primary = execution.ToFailureDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "Failed to commit mounted boot.wim changes.") with
            { Details = "Commit diagnostics:" + Environment.NewLine + execution.ToDiagnosticText() };
        }
        catch (Exception ex)
        {
            _nativeTerminationConfirmed = WinPeMountRecovery.IsTerminationConfirmed(ex);
            primary = new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed, "Failed to commit mounted boot.wim changes.", exception: ex);
        }
        if (!_nativeTerminationConfirmed) State = WinPeMountState.RecoveryRequired;
        WinPeResult discard = await DiscardAsync(CancellationToken.None).ConfigureAwait(false);
        return WinPeResult.Failure(WinPeMountRecovery.Combine(primary, discard));
    }

    public async Task<WinPeResult> DiscardAsync(CancellationToken cancellationToken)
    {
        if (State == WinPeMountState.Unmounted) return WinPeResult.Success();
        using var cleanup = new CancellationTokenSource(WinPeMountRecovery.CleanupTimeout);
        if (State == WinPeMountState.RecoveryRequired)
            return await ReconcileAsync(cleanup.Token).ConfigureAwait(false);
        WinPeDiagnostic primary;
        try
        {
            WinPeProcessExecution execution = await _processRunner.RunAsync(_dismPath,
                ["/Unmount-Image", $"/MountDir:{MountDirectoryPath}", "/Discard"], _workingDirectory,
                cleanup.Token, executionTimeout: WinPeMountRecovery.CleanupTimeout).ConfigureAwait(false);
            if (execution.IsSuccess)
            {
                State = WinPeMountState.Unmounted;
                CleanupDiagnostic = null;
                return WinPeResult.Success();
            }
            primary = execution.ToFailureDiagnostic(WinPeErrorCodes.WimUnmountFailed, "Failed to discard mounted boot.wim changes.");
        }
        catch (Exception ex)
        {
            _nativeTerminationConfirmed = WinPeMountRecovery.IsTerminationConfirmed(ex);
            primary = new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed, "Failed to discard mounted boot.wim changes.", exception: ex);
        }
        State = WinPeMountState.RecoveryRequired;
        WinPeResult<bool> inventory = await WinPeMountRecovery.InspectAsync(_processRunner, _dismPath, BootWimPath,
            MountDirectoryPath, _workingDirectory, cleanup.Token).ConfigureAwait(false);
        if (inventory.Error?.Exception is { } queryError && !WinPeMountRecovery.IsTerminationConfirmed(queryError))
            _nativeTerminationConfirmed = false;
        if (_nativeTerminationConfirmed && inventory.IsSuccess && !inventory.Value)
        {
            State = WinPeMountState.Unmounted;
            CleanupDiagnostic = primary;
        }
        else
        {
            CleanupDiagnostic = Ownership(primary) with { CleanupDiagnostic = inventory.Error };
        }
        return WinPeResult.Failure(CleanupDiagnostic);
    }

    public async Task<WinPeResult> ReconcileAsync(CancellationToken cleanupToken)
    {
        if (State == WinPeMountState.Unmounted) return WinPeResult.Success();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cleanupToken);
        bounded.CancelAfter(WinPeMountRecovery.CleanupTimeout);
        WinPeResult result = await WinPeMountRecovery.ReconcileOwnedMountAsync(_processRunner, _dismPath, BootWimPath,
            MountDirectoryPath, _workingDirectory, bounded.Token, _nativeTerminationConfirmed).ConfigureAwait(false);
        State = result.IsSuccess ? WinPeMountState.Unmounted : WinPeMountState.RecoveryRequired;
        CleanupDiagnostic = result.Error;
        if (result.Error is not null) _nativeTerminationConfirmed = result.Error.NativeTerminationConfirmed;
        return result;
    }

    internal async Task<WinPeResult> FailAsync(WinPeDiagnostic primary)
    {
        if (primary.RecoveryRequired && !primary.NativeTerminationConfirmed ||
            primary.Exception is not null && !WinPeMountRecovery.IsTerminationConfirmed(primary.Exception))
        {
            _nativeTerminationConfirmed = false;
            State = WinPeMountState.RecoveryRequired;
        }
        return WinPeResult.Failure(WinPeMountRecovery.Combine(primary, await DiscardAsync(CancellationToken.None).ConfigureAwait(false)));
    }

    private WinPeDiagnostic Ownership(WinPeDiagnostic error) =>
        WinPeMountRecovery.WithOwnership(error, BootWimPath, MountDirectoryPath, _nativeTerminationConfirmed);

    public async ValueTask DisposeAsync()
    {
        if (State == WinPeMountState.Mounted)
            await DiscardAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
