// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Creates WinPE workspaces by invoking ADK Copype and preparing Foundry working folders.
/// </summary>
public sealed class WinPeBuildService : IWinPeBuildService
{
    private readonly WinPeToolResolver _toolResolver;
    private readonly IWinPeProcessRunner _processRunner;
    private readonly WinPeWorkspaceCleanupService _workspaceCleanup;

    /// <summary>
    /// Initializes a WinPE build service using the default ADK tool resolver and process runner.
    /// </summary>
    public WinPeBuildService()
        : this(new WinPeToolResolver(), new WinPeProcessRunner())
    {
    }

    internal WinPeBuildService(WinPeToolResolver toolResolver, IWinPeProcessRunner processRunner,
        WinPeWorkspaceCleanupService? workspaceCleanup = null)
    {
        _toolResolver = toolResolver;
        _processRunner = processRunner;
        _workspaceCleanup = workspaceCleanup ?? new WinPeWorkspaceCleanupService();
    }

    /// <inheritdoc />
    public async Task<WinPeResult<WinPeBuildArtifact>> BuildAsync(
        WinPeBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateBuildOptions(options);
        if (validationError is not null)
        {
            return WinPeResult<WinPeBuildArtifact>.Failure(validationError);
        }

        WinPeResult<WinPeToolPaths> toolsResult = _toolResolver.ResolveTools(ResolveAdkRootHint(options));
        if (!toolsResult.IsSuccess)
        {
            return WinPeResult<WinPeBuildArtifact>.Failure(toolsResult.Error!);
        }

        WinPeToolPaths tools = toolsResult.Value!;
        string workingDirectory = ResolveWorkingDirectory(options);

        try
        {
            if (Directory.Exists(workingDirectory) && options.CleanExistingWorkingDirectory)
            {
                WinPeResult cleanup = _workspaceCleanup.Delete(workingDirectory);
                if (!cleanup.IsSuccess) return WinPeResult<WinPeBuildArtifact>.Failure(cleanup.Error!);
            }

            Directory.CreateDirectory(options.OutputDirectoryPath);

            WinPeProcessExecution copyPeResult = await _processRunner.RunCmdScriptAsync(
                tools.CopypePath,
                $"{options.Architecture.ToCopypeArchitecture()} {WinPeProcessRunner.Quote(workingDirectory)}",
                options.OutputDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!copyPeResult.IsSuccess)
            {
                WinPeDiagnostic copypeFailure = copyPeResult.ToFailureDiagnostic(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to create WinPE workspace using copype.cmd.",
                    "Build WinPE workspace",
                    "copype");
                if (copypeFailure.FailureDetail == WinPeCopypeFailureDetails.WimMountFailed)
                {
                    copypeFailure = await AddDismMountReplayAsync(
                        copypeFailure,
                        tools.DismPath,
                        workingDirectory,
                        cancellationToken).ConfigureAwait(false);
                }

                return WinPeResult<WinPeBuildArtifact>.Failure(copypeFailure);
            }

            string mediaDirectory = Path.Combine(workingDirectory, "media");
            string bootWimPath = Path.Combine(mediaDirectory, "sources", "boot.wim");
            if (!File.Exists(bootWimPath))
            {
                return WinPeResult<WinPeBuildArtifact>.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "WinPE workspace was created but boot.wim was not found.",
                    $"Expected path: '{bootWimPath}'.",
                    stage: "Build WinPE workspace",
                    exitCode: copyPeResult.ExitCode,
                    failureKind: WinPeFailureKinds.Process,
                    failureReason: WinPeFailureReasons.ArtifactMissing,
                    toolName: "copype");
            }

            string mountDirectory = Path.Combine(workingDirectory, "mount");
            string driverWorkspace = Path.Combine(workingDirectory, "drivers");
            string logsDirectory = Path.Combine(workingDirectory, "logs");
            string tempDirectory = Path.Combine(workingDirectory, "temp");

            Directory.CreateDirectory(mountDirectory);
            Directory.CreateDirectory(driverWorkspace);
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(tempDirectory);

            return WinPeResult<WinPeBuildArtifact>.Success(new WinPeBuildArtifact
            {
                WorkingDirectoryPath = workingDirectory,
                MediaDirectoryPath = mediaDirectory,
                BootWimPath = bootWimPath,
                MountDirectoryPath = mountDirectory,
                DriverWorkspacePath = driverWorkspace,
                LogsDirectoryPath = logsDirectory,
                MakeWinPeMediaPath = tools.MakeWinPeMediaPath,
                DismPath = tools.DismPath,
                Architecture = options.Architecture,
                SignatureMode = options.SignatureMode
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return WinPeResult<WinPeBuildArtifact>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Unexpected failure while creating the WinPE workspace.",
                ex.ToString(),
                stage: "Build WinPE workspace",
                failureReason: ex is UnauthorizedAccessException
                    ? WinPeFailureReasons.AccessDenied
                    : WinPeFailureReasons.ProcessStartFailed,
                toolName: "copype",
                exception: ex);
        }
    }

    /// <summary>
    /// Replays the copype WIM mount with DISM, because copype discards the DISM output and only reports that the
    /// mount failed. The replay adds diagnostics only: the copype failure is always returned, and it takes the DISM
    /// failure reason when the replay fails too. A replay that mounts the image is discarded immediately.
    /// </summary>
    private async Task<WinPeDiagnostic> AddDismMountReplayAsync(
        WinPeDiagnostic copypeFailure,
        string dismPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // copype leaves the staged boot.wim in place when its mount step fails.
        string bootWimPath = Path.Combine(workingDirectory, "media", "sources", "boot.wim");
        if (!File.Exists(bootWimPath))
        {
            return copypeFailure;
        }

        string replayDetails;
        string failureReason = copypeFailure.FailureReason;
        WinPeMountCleanupStatus? mountCleanupStatus = copypeFailure.MountCleanupStatus;
        try
        {
            WinPeResult<WinPeMountSession> mount = await WinPeMountSession.MountAsync(
                _processRunner,
                dismPath,
                bootWimPath,
                Path.Combine(workingDirectory, "mount"),
                workingDirectory,
                cancellationToken).ConfigureAwait(false);

            if (mount.IsSuccess)
            {
                WinPeResult discard = await mount.Value!.DiscardAsync().ConfigureAwait(false);
                replayDetails = "DISM mounted boot.wim, so the copype mount failure did not reproduce.";
                if (!discard.IsSuccess)
                {
                    replayDetails = $"{replayDetails} Discarding the replay mount failed:{Environment.NewLine}{discard.Error?.Details}";
                    mountCleanupStatus = discard.Error?.MountCleanupStatus;
                }
            }
            else
            {
                replayDetails = mount.Error!.Details ?? string.Empty;
                failureReason = mount.Error.FailureReason;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            replayDetails = $"DISM mount replay could not run: {ex.Message}";
        }

        return copypeFailure with
        {
            Details = string.Join(Environment.NewLine, copypeFailure.Details, "DISM mount replay:", replayDetails),
            FailureReason = failureReason,
            MountCleanupStatus = mountCleanupStatus
        };
    }

    private static WinPeDiagnostic? ValidateBuildOptions(WinPeBuildOptions? options)
    {
        if (options is null)
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Build options are required.",
                "Provide a non-null WinPeBuildOptions instance.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectoryPath))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Output directory path is required.",
                "Set WinPeBuildOptions.OutputDirectoryPath to a writable destination folder.");
        }

        if (!Enum.IsDefined(options.Architecture))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Architecture value is invalid.",
                $"Value: '{options.Architecture}'.");
        }

        if (!Enum.IsDefined(options.SignatureMode))
        {
            return new WinPeDiagnostic(
                WinPeErrorCodes.ValidationFailed,
                "Signature mode value is invalid.",
                $"Value: '{options.SignatureMode}'.");
        }

        return null;
    }

    private static string ResolveAdkRootHint(WinPeBuildOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.AdkRootPath))
        {
            return options.AdkRootPath;
        }

        return options.SourceDirectoryPath;
    }

    private static string ResolveWorkingDirectory(WinPeBuildOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.WorkingDirectoryPath))
        {
            return options.WorkingDirectoryPath;
        }

        string folderName = $"FoundryWinPe_{options.Architecture.ToString().ToLowerInvariant()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        return Path.Combine(options.OutputDirectoryPath, folderName);
    }
}
