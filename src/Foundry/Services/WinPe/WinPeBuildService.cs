using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

public sealed class WinPeBuildService : IWinPeBuildService
{
    private readonly WinPeToolResolver _toolResolver = new();
    private readonly WinPeProcessRunner _processRunner = new();
    private readonly ILogger<WinPeBuildService> _logger;

    public WinPeBuildService(ILogger<WinPeBuildService> logger)
    {
        _logger = logger;
    }

    public async Task<WinPeResult<WinPeBuildArtifact>> BuildAsync(
        WinPeBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        WinPeDiagnostic? validationError = ValidateBuildOptions(options);
        if (validationError is not null)
        {
            _logger.LogWarning("WinPE build validation failed. Code={ErrorCode}, Message={ErrorMessage}",
                validationError.Code,
                validationError.Message);
            return WinPeResult<WinPeBuildArtifact>.Failure(validationError);
        }

        _logger.LogInformation(
            "Starting WinPE workspace build. OutputDirectoryPath={OutputDirectoryPath}, Architecture={Architecture}, SignatureMode={SignatureMode}",
            options.OutputDirectoryPath,
            options.Architecture,
            options.SignatureMode);

        WinPeResult<WinPeToolPaths> toolsResult = _toolResolver.ResolveTools(ResolveAdkRootHint(options));
        if (!toolsResult.IsSuccess)
        {
            _logger.LogWarning("Failed to resolve ADK tooling for WinPE build. Code={ErrorCode}, Message={ErrorMessage}",
                toolsResult.Error?.Code,
                toolsResult.Error?.Message);
            return WinPeResult<WinPeBuildArtifact>.Failure(toolsResult.Error!);
        }

        WinPeToolPaths tools = toolsResult.Value!;

        string workingDirectory = ResolveWorkingDirectory(options);
        try
        {
            if (Directory.Exists(workingDirectory) && options.CleanExistingWorkingDirectory)
            {
                _logger.LogDebug("Cleaning existing WinPE working directory: {WorkingDirectoryPath}", workingDirectory);
                Directory.Delete(workingDirectory, recursive: true);
            }

            Directory.CreateDirectory(options.OutputDirectoryPath);

            WinPeProcessExecution copyPeResult = await _processRunner.RunCmdScriptAsync(
                tools.CopypePath,
                $"{options.Architecture.ToCopypeArchitecture()} {WinPeProcessRunner.Quote(workingDirectory)}",
                options.OutputDirectoryPath,
                cancellationToken).ConfigureAwait(false);

            if (!copyPeResult.IsSuccess)
            {
                _logger.LogWarning("copype.cmd failed while creating WinPE workspace. Diagnostic={Diagnostic}", copyPeResult.ToDiagnosticText());
                return WinPeResult<WinPeBuildArtifact>.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to create WinPE workspace using copype.cmd.",
                    copyPeResult.ToDiagnosticText());
            }

            string mediaDirectory = Path.Combine(workingDirectory, "media");
            string bootWimPath = Path.Combine(mediaDirectory, "sources", "boot.wim");
            if (!File.Exists(bootWimPath))
            {
                _logger.LogWarning("WinPE workspace is missing boot.wim. ExpectedPath={BootWimPath}", bootWimPath);
                return WinPeResult<WinPeBuildArtifact>.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "WinPE workspace was created but boot.wim was not found.",
                    $"Expected path: '{bootWimPath}'.");
            }

            string mountDirectory = Path.Combine(workingDirectory, "mount");
            string driverWorkspace = Path.Combine(workingDirectory, "drivers");
            string logsDirectory = Path.Combine(workingDirectory, "logs");

            Directory.CreateDirectory(mountDirectory);
            Directory.CreateDirectory(driverWorkspace);
            Directory.CreateDirectory(logsDirectory);

            _logger.LogInformation("WinPE workspace build completed. WorkingDirectoryPath={WorkingDirectoryPath}", workingDirectory);
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
            _logger.LogError(ex, "Unexpected failure while creating WinPE workspace. WorkingDirectoryPath={WorkingDirectoryPath}", workingDirectory);
            return WinPeResult<WinPeBuildArtifact>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Unexpected failure while creating the WinPE workspace.",
                ex.Message);
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
            return options.WorkingDirectoryPath;
        }

        string folderName = $"FoundryWinPe_{options.Architecture.ToString().ToLowerInvariant()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        return Path.Combine(options.OutputDirectoryPath, folderName);
    }
}
