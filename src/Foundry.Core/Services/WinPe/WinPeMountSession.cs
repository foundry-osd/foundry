// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Owns a mounted image until DISM confirms a successful commit or discard.
/// A completed cleanup failure can be retried; an uncertain process exit blocks further servicing.
/// </summary>
public sealed class WinPeMountSession : IAsyncDisposable
{
    /// <summary>
    /// Identifies persistent cleanup attempts whose process exit is not confirmed.
    /// Workspace deletion must preserve these markers and their containing workspace.
    /// </summary>
    internal const string CleanupMarkerPattern = ".foundry-mount-cleanup-*.pending";

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromMinutes(5);
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
        { Details = details });
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
            WinPeProcessExecution discardResult = await WinPeDismProcessRunner.RunAsync(
                _processRunner,
                _dismPath,
                $"/Unmount-Image /MountDir:{WinPeProcessRunner.Quote(MountDirectoryPath)} /Discard",
                _workingDirectory,
                "Discarding mounted image with DISM.",
                progress: null,
                cleanup.Token).ConfigureAwait(false);

            // Cancellation only attempts process termination; only a returned result confirms normal exit.
            File.Delete(markerPath);
            if (discardResult.IsSuccess)
            {
                _isMounted = false;
                return WinPeResult.Success();
            }

            return WinPeResult.Failure(discardResult.ToFailureDiagnostic(
                WinPeErrorCodes.WimUnmountFailed,
                "Failed to discard mounted boot.wim changes.",
                stage: "Discard boot image changes",
                toolName: "dism.exe"));
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
            var diagnostic = new WinPeDiagnostic(
                WinPeErrorCodes.WimUnmountFailed,
                "Failed to discard mounted boot.wim changes.",
                details,
                stage: "Discard boot image changes",
                failureKind: timedOut ? WinPeFailureKinds.Process : null,
                failureReason: timedOut ? WinPeFailureReasons.Timeout : null,
                toolName: "dism.exe",
                exception: exception);
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
