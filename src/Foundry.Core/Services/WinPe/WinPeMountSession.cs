// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog;

namespace Foundry.Core.Services.WinPe;

/// <summary>Tracks whether a mounted image permits another servicing operation.</summary>
public enum WinPeMountCleanupStatus
{
    /// <summary>The session still owns a mounted image and has not completed cleanup.</summary>
    Mounted,
    /// <summary>DISM confirmed a successful commit or discard.</summary>
    Completed,
    /// <summary>Cleanup failed with known process exit or before process start; the image remains mounted.</summary>
    Failed,
    /// <summary>Cleanup process exit is uncertain; further servicing is blocked.</summary>
    ExitUnconfirmed
}

/// <summary>
/// Owns a mounted image until DISM confirms a successful commit or discard.
/// A completed cleanup failure can be retried; an uncertain process exit blocks further servicing.
/// </summary>
public sealed class WinPeMountSession : IAsyncDisposable
{
    /// <summary>
    /// Identifies persistent cleanup attempts whose process exit is not confirmed.
    /// Workspace deletion must preserve these markers and their containing workspace. A retained marker is removed
    /// only by <see cref="WinPeWorkspaceCleanupService.RecoverUnresolvedMountCleanupsAsync"/>, once Windows proves
    /// that the image is no longer mounted.
    /// </summary>
    internal const string CleanupMarkerPattern = ".foundry-mount-cleanup-*.pending";

    /// <summary>
    /// Bounds every DISM discard, including the retry made when a retained cleanup marker is recovered.
    /// </summary>
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(15);
    private readonly IWinPeProcessRunner _processRunner;
    private readonly string _dismPath;
    private readonly string _workingDirectory;
    private readonly TimeProvider _timeProvider;
    private bool _isMounted;
    private WinPeDiagnostic? _unresolvedCleanupFailure;

    private WinPeMountSession(
        IWinPeProcessRunner processRunner,
        string dismPath,
        string bootWimPath,
        string mountDirectoryPath,
        string workingDirectory,
        TimeProvider timeProvider)
    {
        _processRunner = processRunner;
        _dismPath = dismPath;
        BootWimPath = bootWimPath;
        MountDirectoryPath = mountDirectoryPath;
        _workingDirectory = workingDirectory;
        _timeProvider = timeProvider;
        _isMounted = true;
    }

    public string BootWimPath { get; }
    public string MountDirectoryPath { get; }

    /// <summary>Gets the latest cleanup outcome independently of the primary operation failure.</summary>
    public WinPeMountCleanupStatus CleanupStatus { get; private set; } = WinPeMountCleanupStatus.Mounted;

    /// <summary>Gets the latest unsuccessful cleanup attempt for final orchestration diagnostics.</summary>
    internal WinPeDiagnostic? CleanupFailure { get; private set; }

    public static Task<WinPeResult<WinPeMountSession>> MountAsync(
        IWinPeProcessRunner processRunner,
        string dismPath,
        string bootWimPath,
        string mountDirectoryPath,
        string workingDirectory,
        CancellationToken cancellationToken,
        IProgress<WinPeDismProgress>? dismProgress = null)
    {
        return MountAsync(processRunner, dismPath, bootWimPath, mountDirectoryPath, workingDirectory,
            cancellationToken, TimeProvider.System, dismProgress);
    }

    /// <summary>
    /// Uses the supplied clock for cleanup deadlines without changing normal servicing cancellation.
    /// </summary>
    internal static async Task<WinPeResult<WinPeMountSession>> MountAsync(
        IWinPeProcessRunner processRunner,
        string dismPath,
        string bootWimPath,
        string mountDirectoryPath,
        string workingDirectory,
        CancellationToken cancellationToken,
        TimeProvider timeProvider,
        IProgress<WinPeDismProgress>? dismProgress = null)
    {
        Directory.CreateDirectory(mountDirectoryPath);

        string args = $"/Mount-Image /ImageFile:{WinPeProcessRunner.Quote(bootWimPath)} /Index:1 /MountDir:{WinPeProcessRunner.Quote(mountDirectoryPath)}";
        WinPeProcessExecution mountResult = await WinPeDismProcessRunner.RunAsync(
            processRunner,
            dismPath,
            args,
            workingDirectory,
            "Mounting image with DISM.",
            dismProgress,
            cancellationToken).ConfigureAwait(false);

        if (!mountResult.IsSuccess)
        {
            return WinPeResult<WinPeMountSession>.Failure(mountResult.ToFailureDiagnostic(
                WinPeErrorCodes.WimMountFailed,
                "Failed to mount boot.wim.",
                stage: "Mount boot image",
                toolName: "dism.exe"));
        }

        return WinPeResult<WinPeMountSession>.Success(new WinPeMountSession(
            processRunner,
            dismPath,
            bootWimPath,
            mountDirectoryPath,
            workingDirectory,
            timeProvider));
    }

    public async Task<WinPeResult> CommitAsync(
        CancellationToken cancellationToken,
        IProgress<WinPeDismProgress>? dismProgress = null)
    {
        if (_unresolvedCleanupFailure is not null)
        {
            return WinPeResult.Failure(_unresolvedCleanupFailure);
        }

        if (!_isMounted)
        {
            return WinPeResult.Success();
        }

        WinPeProcessExecution commitResult = await WinPeDismProcessRunner.RunAsync(
            _processRunner,
            _dismPath,
            $"/Unmount-Image /MountDir:{WinPeProcessRunner.Quote(MountDirectoryPath)} /Commit",
            _workingDirectory,
            "Committing image changes with DISM.",
            dismProgress,
            cancellationToken).ConfigureAwait(false);

        if (commitResult.IsSuccess)
        {
            _isMounted = false;
            CleanupStatus = WinPeMountCleanupStatus.Completed;
            CleanupFailure = null;
            return WinPeResult.Success();
        }

        WinPeResult discardResult = await DiscardAsync().ConfigureAwait(false);

        string details = string.Join(
            Environment.NewLine,
            "Commit failed and discard fallback was attempted.",
            "Commit diagnostics:",
            commitResult.ToDiagnosticText(),
            "Discard diagnostics:",
            discardResult.IsSuccess ? "DISM discard completed successfully." : discardResult.Error?.Details);

        return WinPeResult.Failure(commitResult.ToFailureDiagnostic(
            WinPeErrorCodes.WimUnmountFailed,
            "Failed to commit mounted boot.wim changes.",
            stage: "Commit boot image changes",
            toolName: "dism.exe") with
        { Details = details, MountCleanupStatus = CleanupStatus });
    }

    /// <summary>
    /// Attempts discard with its own finite lifetime, independent of operation cancellation.
    /// A persistent marker prevents workspace deletion if the runner cannot confirm process exit.
    /// Completed nonzero exits remain retryable; an uncertain exit blocks further attempts.
    /// </summary>
    public async Task<WinPeResult> DiscardAsync()
    {
        if (_unresolvedCleanupFailure is not null)
        {
            return WinPeResult.Failure(_unresolvedCleanupFailure);
        }

        if (!_isMounted)
        {
            return WinPeResult.Success();
        }

        using var cleanup = new CancellationTokenSource(CleanupTimeout, _timeProvider);
        string markerPath = Path.Combine(_workingDirectory, $".foundry-mount-cleanup-{Guid.NewGuid():N}.pending");
        bool markerCreated = false;
        try
        {
            File.WriteAllText(markerPath, MountDirectoryPath);
            markerCreated = true;
            WinPeProcessExecution discardResult = await RunDiscardAsync(
                _processRunner,
                _dismPath,
                MountDirectoryPath,
                _workingDirectory,
                cleanup.Token).ConfigureAwait(false);

            // Cancellation only attempts process termination; only a returned result confirms normal exit.
            File.Delete(markerPath);
            if (discardResult.IsSuccess)
            {
                _isMounted = false;
                CleanupStatus = WinPeMountCleanupStatus.Completed;
                CleanupFailure = null;
                return WinPeResult.Success();
            }

            CleanupStatus = WinPeMountCleanupStatus.Failed;
            CleanupFailure = discardResult.ToFailureDiagnostic(
                WinPeErrorCodes.WimUnmountFailed,
                "Failed to discard mounted boot.wim changes.",
                stage: "Discard boot image changes",
                toolName: "dism.exe") with
            { MountCleanupStatus = CleanupStatus };
            return WinPeResult.Failure(CleanupFailure);
        }
        catch (Exception exception)
        {
            bool timedOut = exception is OperationCanceledException && cleanup.IsCancellationRequested;
            string details = timedOut
                ? $"DISM discard exceeded the cleanup deadline of {CleanupTimeout}. {exception.Message}"
                : exception.ToString();
            if (markerCreated)
            {
                details = $"{details}{Environment.NewLine}Cleanup is unresolved. Further servicing is blocked. Retained cleanup marker: '{markerPath}'.";
            }
            CleanupStatus = markerCreated ? WinPeMountCleanupStatus.ExitUnconfirmed : WinPeMountCleanupStatus.Failed;
            var diagnostic = new WinPeDiagnostic(
                WinPeErrorCodes.WimUnmountFailed,
                "Failed to discard mounted boot.wim changes.",
                details,
                stage: "Discard boot image changes",
                failureKind: timedOut ? WinPeFailureKinds.Process : null,
                failureReason: timedOut ? WinPeFailureReasons.Timeout : null,
                toolName: "dism.exe",
                exception: exception) with
            { MountCleanupStatus = CleanupStatus };
            CleanupFailure = diagnostic;
            if (markerCreated)
            {
                _unresolvedCleanupFailure = diagnostic;
                Log.ForContext<WinPeMountSession>().Warning(exception,
                    "Mounted image cleanup remains unresolved; workspace deletion and further servicing are blocked. MountDirectory={MountDirectory}, CleanupMarker={CleanupMarker}",
                    MountDirectoryPath, markerPath);
            }
            return WinPeResult.Failure(diagnostic);
        }
    }

    /// <summary>
    /// Runs the DISM discard for a mount directory. A live session and the recovery of a retained cleanup marker
    /// share this command so both unmount an image the same way.
    /// </summary>
    internal static Task<WinPeProcessExecution> RunDiscardAsync(
        IWinPeProcessRunner processRunner,
        string dismPath,
        string mountDirectoryPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        return WinPeDismProcessRunner.RunAsync(
            processRunner,
            dismPath,
            $"/Unmount-Image /MountDir:{WinPeProcessRunner.Quote(mountDirectoryPath)} /Discard",
            workingDirectory,
            "Discarding mounted image with DISM.",
            progress: null,
            cancellationToken);
    }

    /// <summary>
    /// Makes one best-effort discard attempt when process exit is known, without replacing a primary failure.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isMounted)
        {
            WinPeResult result = await DiscardAsync().ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Log.ForContext<WinPeMountSession>().Warning(result.Error?.Exception,
                    "Mounted image cleanup did not complete. MountDirectory={MountDirectory}, FailureReason={FailureReason}, Details={Details}",
                    MountDirectoryPath, result.Error?.FailureReason, result.Error?.Details);
            }
        }
    }
}
