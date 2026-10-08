// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

namespace Foundry.Core.Services.WinPe;

public sealed class WinPeIsoMediaService : IWinPeIsoMediaService
{
    /// <summary>
    /// Gets the diagnostic stage reported when the generated ISO cannot be moved to the requested output path.
    /// </summary>
    public const string FinalizeOutputStage = "Finalize ISO output";

    private const int FinalizeMoveAttempts = 3;
    private static readonly TimeSpan DefaultFinalizeMoveRetryDelay = TimeSpan.FromSeconds(1);

    private readonly IWinPeProcessRunner _processRunner;
    private readonly Action<string, string, bool> _moveFile;
    private readonly TimeSpan _finalizeMoveRetryDelay;

    public WinPeIsoMediaService()
        : this(new WinPeProcessRunner())
    {
    }

    internal WinPeIsoMediaService(IWinPeProcessRunner processRunner)
        : this(processRunner, File.Move, DefaultFinalizeMoveRetryDelay)
    {
    }

    internal WinPeIsoMediaService(
        IWinPeProcessRunner processRunner,
        Action<string, string, bool> moveFile,
        TimeSpan finalizeMoveRetryDelay)
    {
        _processRunner = processRunner;
        _moveFile = moveFile;
        _finalizeMoveRetryDelay = finalizeMoveRetryDelay;
    }

    public async Task<WinPeResult> CreateAsync(
        WinPeIsoMediaOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateOptions(options);
        if (validationError is not null)
        {
            return WinPeResult.Failure(validationError);
        }

        WinPeWorkspacePreparationResult preparedWorkspace = options.PreparedWorkspace!;
        string requestedOutputPath = options.OutputIsoPath.Trim();
        string? preparedOutputPath = null;
        string? safeWorkspacePath = null;
        string currentStage = "Prepare ISO output path";
        bool hasExternalContent = options.CustomImages is not null || options.PostInstallation is not null;
        string isoTool = hasExternalContent ? "Oscdimg" : "MakeWinPEMedia";
        StrongBox<int> finalizeRetryCount = new(0);

        try
        {
            ReportProgress(options.Progress, 0, "Preparing ISO output path.");
            EnsureOutputDirectoryExists(requestedOutputPath);
            if (!options.ForceOverwriteOutput && File.Exists(requestedOutputPath))
                throw new IOException("The ISO output already exists and overwrite is disabled.");
            preparedOutputPath = PrepareOutputPath(requestedOutputPath, options.IsoTempDirectoryPath);
            currentStage = "Prepare ISO workspace";
            ReportProgress(options.Progress, 20, "Preparing ISO workspace.");
            string makeWinPeMediaWorkspacePath;
            if (hasExternalContent)
            {
                if (options.CustomImages is not null)
                    WinPeCustomImageMediaService.ValidateConfigurationBinding(options.CustomImages, options.DeployConfigurationJson ?? string.Empty);
                if (options.PostInstallation is not null)
                    WinPePreOobeMediaService.ValidateConfigurationBinding(options.PostInstallation, options.DeployConfigurationJson ?? string.Empty);
                _ = WinPeCustomImageIsoMastering.ResolveOscdimg(preparedWorkspace.Tools);
                string temporaryRoot = string.IsNullOrWhiteSpace(options.IsoTempDirectoryPath)
                    ? Path.GetDirectoryName(preparedOutputPath)! : options.IsoTempDirectoryPath;
                safeWorkspacePath = Path.Combine(temporaryRoot, "custom-iso-" + Guid.NewGuid().ToString("N"));
                await WinPeCustomImageIsoMastering.PrepareAsync(preparedWorkspace.Artifact, options.CustomImages, options.PostInstallation,
                    safeWorkspacePath, preparedOutputPath, requestedOutputPath, cancellationToken).ConfigureAwait(false);
                makeWinPeMediaWorkspacePath = safeWorkspacePath;
            }
            else
            {
                makeWinPeMediaWorkspacePath = PrepareWorkspacePath(
                    preparedWorkspace.Artifact.WorkingDirectoryPath,
                    options.IsoTempDirectoryPath,
                    out safeWorkspacePath);
            }

            string arguments =
                $"/ISO /F {WinPeProcessRunner.Quote(makeWinPeMediaWorkspacePath)} {WinPeProcessRunner.Quote(preparedOutputPath)}" +
                (preparedWorkspace.UseBootEx ? " /bootex" : string.Empty);

            currentStage = $"Run {isoTool} for ISO";
            ReportProgress(options.Progress, 40, hasExternalContent ? "Creating ISO media." : "Running MakeWinPEMedia for ISO.");
            WinPeProcessExecution execution = !hasExternalContent
                ? await _processRunner.RunCmdScriptAsync(preparedWorkspace.Tools.MakeWinPeMediaPath,
                    arguments, makeWinPeMediaWorkspacePath, cancellationToken).ConfigureAwait(false)
                : await _processRunner.RunAsync(WinPeCustomImageIsoMastering.ResolveOscdimg(preparedWorkspace.Tools),
                    WinPeCustomImageIsoMastering.CreateArguments(makeWinPeMediaWorkspacePath, preparedOutputPath, preparedWorkspace.UseBootEx),
                    makeWinPeMediaWorkspacePath, cancellationToken).ConfigureAwait(false);

            if (!execution.IsSuccess)
            {
                return WinPeResult.Failure(execution.ToFailureDiagnostic(
                    WinPeErrorCodes.IsoCreateFailed,
                    "Failed to create WinPE ISO media.",
                    "Create ISO media",
                    isoTool));
            }

            if (!File.Exists(preparedOutputPath) || new FileInfo(preparedOutputPath).Length == 0)
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.IsoCreateFailed,
                    $"{isoTool} completed without producing the expected ISO artifact.",
                    execution.ToDiagnosticText(),
                    stage: "Create ISO media",
                    exitCode: execution.ExitCode,
                    failureKind: WinPeFailureKinds.Process,
                    failureReason: WinPeFailureReasons.ArtifactMissing,
                    toolName: isoTool);
            }

            currentStage = FinalizeOutputStage;
            ReportProgress(options.Progress, 90, "Finalizing ISO output.");
            cancellationToken.ThrowIfCancellationRequested();
            await FinalizeOutputAsync(preparedOutputPath, requestedOutputPath, options.ForceOverwriteOutput,
                finalizeRetryCount, cancellationToken).ConfigureAwait(false);
            ReportProgress(options.Progress, 100, "ISO media completed.");
            return WinPeResult.Success();
        }
        catch (Exception ex) when (currentStage == FinalizeOutputStage && IsOutputLockedOrDenied(ex))
        {
            // Sharing and lock violations are reported as access_denied so this user-actionable
            // condition stays comparable with existing ISO finalization telemetry.
            return WinPeResult.Failure(
                WinPeErrorCodes.IsoCreateFailed,
                "The ISO output file could not be replaced because it is in use, read-only, or access was denied.",
                ex.ToString(),
                stage: FinalizeOutputStage,
                failureKind: WinPeFailureKinds.FileSystem,
                failureReason: WinPeFailureReasons.AccessDenied,
                retryCount: finalizeRetryCount.Value,
                exception: ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.IsoCreateFailed,
                "Unexpected failure while creating WinPE ISO media.",
                ex.ToString(),
                stage: currentStage,
                failureKind: currentStage == $"Run {isoTool} for ISO"
                    ? WinPeFailureKinds.Process
                    : WinPeFailureKinds.FileSystem,
                failureReason: ex switch
                {
                    UnauthorizedAccessException => WinPeFailureReasons.AccessDenied,
                    IOException => WinPeFailureReasons.IoError,
                    _ when currentStage == $"Run {isoTool} for ISO" => WinPeFailureReasons.ProcessStartFailed,
                    _ => WinPeFailureReasons.Unexpected
                },
                toolName: currentStage == $"Run {isoTool} for ISO" ? isoTool : null,
                exception: ex);
        }
        finally
        {
            CleanupPreparedOutput(requestedOutputPath, preparedOutputPath);
            CleanupPreparedWorkspace(safeWorkspacePath);
        }
    }

    private static WinPeDiagnostic? ValidateOptions(WinPeIsoMediaOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ISO media options are required.",
                "Provide a non-null WinPeIsoMediaOptions instance.");
        }

        if (options.PreparedWorkspace is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Prepared WinPE workspace is required.",
                "Set WinPeIsoMediaOptions.PreparedWorkspace.");
        }

        if (string.IsNullOrWhiteSpace(options.PreparedWorkspace.Tools.MakeWinPeMediaPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "MakeWinPEMedia path is required.",
                "Set WinPeToolPaths.MakeWinPeMediaPath.");
        }

        if (string.IsNullOrWhiteSpace(options.PreparedWorkspace.Artifact.WorkingDirectoryPath) ||
            !Directory.Exists(options.PreparedWorkspace.Artifact.WorkingDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Prepared WinPE workspace directory was not found.",
                $"Path: '{options.PreparedWorkspace.Artifact.WorkingDirectoryPath}'.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputIsoPath) ||
            !options.OutputIsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Output ISO path must end with .iso.",
                $"Path: '{options.OutputIsoPath}'.");
        }

        if ((ContainsNonAscii(options.OutputIsoPath) ||
             ContainsNonAscii(options.PreparedWorkspace.Artifact.WorkingDirectoryPath)) &&
            string.IsNullOrWhiteSpace(options.IsoTempDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "ISO temporary directory path is required for non-ASCII MakeWinPEMedia paths.",
                "Set WinPeIsoMediaOptions.IsoTempDirectoryPath.");
        }

        return null;
    }

    private static string PrepareWorkspacePath(
        string requestedWorkspacePath,
        string isoTempDirectoryPath,
        out string? safeWorkspacePath)
    {
        safeWorkspacePath = null;
        if (!ContainsNonAscii(requestedWorkspacePath))
        {
            return requestedWorkspacePath;
        }

        Directory.CreateDirectory(isoTempDirectoryPath);
        safeWorkspacePath = Path.Combine(isoTempDirectoryPath, $"workspace-{Guid.NewGuid():N}");
        CopyDirectoryContents(requestedWorkspacePath, safeWorkspacePath);
        return safeWorkspacePath;
    }

    private static string PrepareOutputPath(string requestedOutputPath, string isoTempDirectoryPath)
    {
        string directory = ContainsNonAscii(requestedOutputPath)
            ? isoTempDirectoryPath
            : Path.GetDirectoryName(Path.GetFullPath(requestedOutputPath))!;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"foundry-{Guid.NewGuid():N}.pending.iso");
    }

    private async Task FinalizeOutputAsync(
        string preparedOutputPath,
        string requestedOutputPath,
        bool overwrite,
        StrongBox<int> retryCount,
        CancellationToken cancellationToken)
    {
        // Always replace from the destination directory: File.Move across volumes is a copy
        // and must never expose a partially copied final ISO or destroy the prior deliverable.
        string destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(requestedOutputPath))!;
        string candidate = preparedOutputPath;
        bool copyRequired = !string.Equals(Path.GetDirectoryName(Path.GetFullPath(preparedOutputPath)),
            destinationDirectory, StringComparison.OrdinalIgnoreCase);
        if (copyRequired)
            candidate = Path.Combine(destinationDirectory, $"foundry-{Guid.NewGuid():N}.pending.iso");
        try
        {
            if (copyRequired)
            {
                await using FileStream source = new(preparedOutputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using FileStream destination = new(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous);
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await MoveOutputWithRetryAsync(candidate, requestedOutputPath, overwrite, retryCount, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (copyRequired) CleanupPreparedOutput(requestedOutputPath, candidate);
        }
    }

    /// <summary>
    /// Moves the completed ISO over the requested output, retrying briefly because antivirus scanners and
    /// shell previews can hold the existing ISO for a short time. Persistent locks surface after the last attempt.
    /// </summary>
    private async Task MoveOutputWithRetryAsync(
        string candidatePath,
        string requestedOutputPath,
        bool overwrite,
        StrongBox<int> retryCount,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _moveFile(candidatePath, requestedOutputPath, overwrite);
                return;
            }
            catch (Exception ex) when (attempt < FinalizeMoveAttempts && IsOutputLockedOrDenied(ex))
            {
                retryCount.Value = attempt;
                await Task.Delay(_finalizeMoveRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsOutputLockedOrDenied(Exception exception)
    {
        const int ErrorSharingViolation = 32;
        const int ErrorLockViolation = 33;
        return exception is UnauthorizedAccessException
            || (exception is IOException ioException && (ioException.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation);
    }

    private static void CleanupPreparedOutput(string requestedOutputPath, string? preparedOutputPath)
    {
        if (string.IsNullOrWhiteSpace(preparedOutputPath) ||
            string.Equals(preparedOutputPath, requestedOutputPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(preparedOutputPath))
        {
            return;
        }

        try
        {
            File.Delete(preparedOutputPath);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void CleanupPreparedWorkspace(string? safeWorkspacePath)
    {
        if (string.IsNullOrWhiteSpace(safeWorkspacePath) || !Directory.Exists(safeWorkspacePath))
        {
            return;
        }

        try
        {
            Directory.Delete(safeWorkspacePath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void CopyDirectoryContents(string sourceDirectoryPath, string targetDirectoryPath)
    {
        Directory.CreateDirectory(targetDirectoryPath);

        foreach (string directoryPath in Directory.EnumerateDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectoryPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(targetDirectoryPath, relativePath));
        }

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDirectoryPath, filePath);
            string targetFilePath = Path.Combine(targetDirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            File.Copy(filePath, targetFilePath, overwrite: true);
        }
    }

    private static void EnsureOutputDirectoryExists(string outputIsoPath)
    {
        string? outputDirectory = Path.GetDirectoryName(outputIsoPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
    }

    private static bool ContainsNonAscii(string value)
    {
        return value.Any(character => character > 127);
    }

    private static void ReportProgress(IProgress<WinPeMediaProgress>? progress, int percent, string status)
    {
        progress?.Report(new WinPeMediaProgress
        {
            Percent = Math.Clamp(percent, 0, 100),
            Status = status
        });
    }

}
