using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeLocalDeployEmbeddingService : IWinPeLocalDeployEmbeddingService
{
    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeLocalDeployEmbeddingService> _logger;

    public WinPeLocalDeployEmbeddingService(
        WinPeProcessRunner processRunner,
        ILogger<WinPeLocalDeployEmbeddingService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        if (!IsEnabledEnvironmentFlag(Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployEnableEnvironmentVariable)))
        {
            _logger.LogDebug("Skipping local Foundry.Deploy embedding because the feature flag is disabled.");
            return WinPeResult.Success();
        }

        _logger.LogInformation("Provisioning local Foundry.Deploy archive into mounted WinPE image. Architecture={Architecture}", architecture);
        WinPeResult<string> archiveResult = await ResolveLocalDeployArchivePathAsync(architecture, workingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!archiveResult.IsSuccess)
        {
            _logger.LogWarning("Failed to resolve local Foundry.Deploy archive path. Code={ErrorCode}, Message={ErrorMessage}",
                archiveResult.Error?.Code,
                archiveResult.Error?.Message);
            return WinPeResult.Failure(archiveResult.Error!);
        }

        string destinationPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedDeployArchivePathInImage);
        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for local Foundry.Deploy archive.",
                $"Destination: '{destinationPath}'.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            _logger.LogInformation(
                "Copying local Foundry.Deploy archive into mounted WinPE image. ArchivePath={ArchivePath}, DestinationPath={DestinationPath}",
                archiveResult.Value!,
                destinationPath);
            File.Copy(archiveResult.Value!, destinationPath, overwrite: true);
            _logger.LogInformation("Copied local Foundry.Deploy archive into mounted WinPE image successfully. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy local Foundry.Deploy archive into mounted WinPE image. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to copy local Foundry.Deploy archive into mounted WinPE image.",
                ex.ToString());
        }
    }

    private async Task<WinPeResult<string>> ResolveLocalDeployArchivePathAsync(
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string configuredArchivePath = (Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployArchiveEnvironmentVariable) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredArchivePath))
        {
            if (!File.Exists(configuredArchivePath))
            {
                return WinPeResult<string>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Configured local Foundry.Deploy archive was not found.",
                    $"Set {WinPeDefaults.LocalDeployArchiveEnvironmentVariable} to an existing .zip file. Path: '{configuredArchivePath}'.");
            }

            _logger.LogInformation("Using configured local Foundry.Deploy archive. ArchivePath={ArchivePath}", configuredArchivePath);
            return WinPeResult<string>.Success(configuredArchivePath);
        }

        string configuredProjectPath = (Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployProjectEnvironmentVariable) ?? string.Empty).Trim();
        string projectPath;
        if (!string.IsNullOrWhiteSpace(configuredProjectPath))
        {
            if (!File.Exists(configuredProjectPath))
            {
                return WinPeResult<string>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Configured Foundry.Deploy project file was not found.",
                    $"Set {WinPeDefaults.LocalDeployProjectEnvironmentVariable} to an existing .csproj file. Path: '{configuredProjectPath}'.");
            }

            projectPath = configuredProjectPath;
            _logger.LogInformation("Using configured Foundry.Deploy project path. ProjectPath={ProjectPath}", projectPath);
        }
        else if (!TryFindFoundryDeployProjectPath(out projectPath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Unable to locate Foundry.Deploy project for local WinPE embedding.",
                $"Set {WinPeDefaults.LocalDeployArchiveEnvironmentVariable} to a .zip archive or {WinPeDefaults.LocalDeployProjectEnvironmentVariable} to Foundry.Deploy.csproj.");
        }
        else
        {
            _logger.LogInformation("Discovered Foundry.Deploy project path automatically. ProjectPath={ProjectPath}", projectPath);
        }

        string runtimeIdentifier = architecture.ToDotnetRuntimeIdentifier();
        string localWorkspace = Path.Combine(workingDirectoryPath, "FoundryDeployLocal");
        string publishDirectory = Path.Combine(localWorkspace, "publish", runtimeIdentifier);
        string archivePath = Path.Combine(localWorkspace, $"Foundry.Deploy-{runtimeIdentifier}.zip");

        TryDeleteDirectory(publishDirectory);
        Directory.CreateDirectory(publishDirectory);

        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare local workspace for Foundry.Deploy archive generation. ArchivePath={ArchivePath}", archivePath);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to prepare local workspace for Foundry.Deploy archive generation.",
                ex.ToString());
        }

        string publishArgs = string.Join(" ",
            "publish",
            WinPeProcessRunner.Quote(projectPath),
            "-c", "Release",
            "-r", runtimeIdentifier,
            "--self-contained", "true",
            "/p:PublishSingleFile=true",
            "/p:EnableCompressionInSingleFile=true",
            "/p:IncludeNativeLibrariesForSelfExtract=true",
            "/p:IncludeAllContentForSelfExtract=true",
            "/p:DebugType=None",
            "/p:GenerateDocumentationFile=false",
            "-o", WinPeProcessRunner.Quote(publishDirectory));

        _logger.LogInformation(
            "Publishing local Foundry.Deploy payload. RuntimeIdentifier={RuntimeIdentifier}, PublishDirectory={PublishDirectory}",
            runtimeIdentifier,
            publishDirectory);
        WinPeProcessExecution publish = await _processRunner.RunAsync(
            "dotnet",
            publishArgs,
            workingDirectoryPath,
            cancellationToken).ConfigureAwait(false);

        if (!publish.IsSuccess)
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to publish Foundry.Deploy for local WinPE embedding.",
                publish.ToDiagnosticText());
        }
        _logger.LogInformation("Published local Foundry.Deploy payload successfully. PublishDirectory={PublishDirectory}", publishDirectory);

        string executablePath = Path.Combine(publishDirectory, "Foundry.Deploy.exe");
        if (!File.Exists(executablePath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Foundry.Deploy publish output is incomplete.",
                $"Expected executable: '{executablePath}'.");
        }

        try
        {
            _logger.LogInformation("Creating local Foundry.Deploy archive from publish output. ArchivePath={ArchivePath}", archivePath);
            ZipFile.CreateFromDirectory(publishDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Foundry.Deploy archive from publish output. PublishDirectory={PublishDirectory}, ArchivePath={ArchivePath}", publishDirectory, archivePath);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to create Foundry.Deploy archive from local publish output.",
                ex.ToString());
        }

        _logger.LogInformation("Created local Foundry.Deploy archive successfully. ArchivePath={ArchivePath}", archivePath);
        return WinPeResult<string>.Success(archivePath);
    }

    private static bool TryFindFoundryDeployProjectPath(out string projectPath)
    {
        foreach (string root in GetProjectDiscoveryRoots())
        {
            if (TryResolveFoundryDeployProjectPath(root, out projectPath))
            {
                return true;
            }
        }

        projectPath = string.Empty;
        return false;
    }

    private static IEnumerable<string> GetProjectDiscoveryRoots()
    {
        string current = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(current))
        {
            yield return current;
        }

        string baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory) && !baseDirectory.Equals(current, StringComparison.OrdinalIgnoreCase))
        {
            yield return baseDirectory;
        }
    }

    private static bool TryResolveFoundryDeployProjectPath(string startDirectory, out string projectPath)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Foundry.Deploy", "Foundry.Deploy.csproj");
            if (File.Exists(candidate))
            {
                projectPath = candidate;
                return true;
            }

            current = current.Parent;
        }

        projectPath = string.Empty;
        return false;
    }

    private static bool IsEnabledEnvironmentFlag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() switch
        {
            "1" => true,
            "true" => true,
            "TRUE" => true,
            "yes" => true,
            "YES" => true,
            "on" => true,
            "ON" => true,
            _ => false
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
