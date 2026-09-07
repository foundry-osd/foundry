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

    /// <summary>
    /// Initializes a WinPE build service using the default ADK tool resolver and process runner.
    /// </summary>
    public WinPeBuildService()
        : this(new WinPeToolResolver(), new WinPeProcessRunner())
    {
    }

    internal WinPeBuildService(WinPeToolResolver toolResolver, IWinPeProcessRunner processRunner)
    {
        _toolResolver = toolResolver;
        _processRunner = processRunner;
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

        WinPeResult<WinPeToolPaths> toolsResult = _toolResolver.ResolveTools(ResolveAdkRootHint(options), options.Architecture);
        if (!toolsResult.IsSuccess)
        {
            return WinPeResult<WinPeBuildArtifact>.Failure(toolsResult.Error!);
        }

        WinPeToolPaths tools = toolsResult.Value!;
        string workingDirectory = ResolveWorkingDirectory(options);
        string bootWimPath = Path.Combine(workingDirectory, "media", "sources", "boot.wim");
        string mountDirectory = Path.Combine(workingDirectory, "mount");
        bool copypeStarted = false;
        bool nativeTerminationConfirmed = true;

        try
        {
            string arguments = $"{options.Architecture.ToCopypeArchitecture()} {WinPeProcessRunner.Quote(workingDirectory)}";
            WinPeProcessRunner.ValidateCmdScript(tools.CopypePath, arguments);

            string outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.OutputDirectoryPath));
            string workingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory));
            if (!workingRoot.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return WinPeResult<WinPeBuildArtifact>.Failure(WinPeErrorCodes.ValidationFailed,
                    "The Copype workspace must be a new child of its output directory.");
            }
            ValidateOrdinaryParents(workingRoot);
            if (Directory.Exists(workingRoot) || File.Exists(workingRoot))
            {
                return WinPeResult<WinPeBuildArtifact>.Failure(WinPeErrorCodes.ValidationFailed,
                    "The requested WinPE workspace already exists and must be preserved.", workingRoot);
            }

            // Copype mounts the source image internally, so inspect it before invoking the script.
            string selectedWinPeRoot = WinPeProcessRunner.BuildAdkEnvironmentOverrides(tools.CopypePath)!["WinPERoot"];
            string sourceImagePath = Path.Combine(selectedWinPeRoot,
                options.Architecture.ToCopypeArchitecture(), "en-us", "winpe.wim");
            if (!File.Exists(sourceImagePath))
            {
                return WinPeResult<WinPeBuildArtifact>.Failure(WinPeErrorCodes.ToolNotFound,
                    "The selected ADK WinPE source image was not found.", sourceImagePath);
            }
            Directory.CreateDirectory(options.OutputDirectoryPath);
            WinPeResult sourceCompatibility = await WinPeToolResolver.ValidateImageAsync(
                tools, sourceImagePath, 1, options.Architecture, _processRunner, options.OutputDirectoryPath,
                cancellationToken, writeDiagnosticLog: false).ConfigureAwait(false);
            if (!sourceCompatibility.IsSuccess)
            {
                return WinPeResult<WinPeBuildArtifact>.Failure(sourceCompatibility.Error!);
            }

            ValidateOrdinaryParents(workingDirectory);
            if (Directory.Exists(workingDirectory) || File.Exists(workingDirectory))
            {
                return WinPeResult<WinPeBuildArtifact>.Failure(WinPeErrorCodes.ValidationFailed,
                    "The requested WinPE workspace appeared before Copype and must be preserved.", workingDirectory);
            }
            copypeStarted = true;
            WinPeProcessExecution copyPeResult = await _processRunner.RunCmdScriptAsync(
                tools.CopypePath,
                arguments,
                options.OutputDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!copyPeResult.IsSuccess)
            {
                return await FailAfterCopypeAsync(copyPeResult.ToFailureDiagnostic(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to create WinPE workspace using copype.cmd.",
                    "Build WinPE workspace",
                    "copype"), true).ConfigureAwait(false);
            }

            string mediaDirectory = Path.Combine(workingDirectory, "media");
            if (!File.Exists(bootWimPath))
            {
                return await FailAfterCopypeAsync(new WinPeDiagnostic(WinPeErrorCodes.BuildFailed,
                    "WinPE workspace was created but boot.wim was not found.",
                    $"Expected path: '{bootWimPath}'."), true).ConfigureAwait(false);
            }

            WinPeResult imageCompatibility = await WinPeToolResolver.ValidateImageAsync(
                tools, bootWimPath, 1, options.Architecture, _processRunner, workingDirectory, cancellationToken).ConfigureAwait(false);
            if (!imageCompatibility.IsSuccess)
            {
                return await FailAfterCopypeAsync(imageCompatibility.Error!, true).ConfigureAwait(false);
            }
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
        catch (Exception ex)
        {
            nativeTerminationConfirmed = WinPeMountRecovery.IsTerminationConfirmed(ex);
            var diagnostic = new WinPeDiagnostic(
                WinPeErrorCodes.BuildFailed,
                "Unexpected failure while creating the WinPE workspace.",
                ex.ToString(),
                stage: "Build WinPE workspace",
                failureKind: ex is TimeoutException ? WinPeFailureKinds.Process : null,
                failureReason: ex switch
                {
                    TimeoutException => WinPeFailureReasons.Timeout,
                    UnauthorizedAccessException => WinPeFailureReasons.AccessDenied,
                    _ => WinPeFailureReasons.ProcessStartFailed
                },
                toolName: "copype",
                errorSummary: ex.Message,
                exception: ex);
            if (copypeStarted)
            {
                return await FailAfterCopypeAsync(diagnostic, nativeTerminationConfirmed).ConfigureAwait(false);
            }
            return WinPeResult<WinPeBuildArtifact>.Failure(diagnostic with
            {
                RecoveryRequired = !nativeTerminationConfirmed,
                NativeTerminationConfirmed = nativeTerminationConfirmed,
                RetainedPaths = !nativeTerminationConfirmed ? [workingDirectory] : []
            });
        }

        async Task<WinPeResult<WinPeBuildArtifact>> FailAfterCopypeAsync(WinPeDiagnostic primary, bool terminated)
        {
            using var cleanup = new CancellationTokenSource(WinPeMountRecovery.CleanupTimeout);
            WinPeResult recovery = await WinPeMountRecovery.ReconcileOwnedMountAsync(_processRunner, tools.DismPath,
                bootWimPath, mountDirectory, options.OutputDirectoryPath, cleanup.Token, terminated).ConfigureAwait(false);
            return WinPeResult<WinPeBuildArtifact>.Failure(WinPeMountRecovery.Combine(primary, recovery));
        }
    }

    private static void ValidateOrdinaryParents(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("WinPE workspace paths must not traverse reparse points.");
            }
        }
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
            return Path.GetFullPath(options.WorkingDirectoryPath);
        }

        string folderName = $"FoundryWinPe_{options.Architecture.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}";
        return Path.GetFullPath(Path.Combine(options.OutputDirectoryPath, folderName));
    }
}
