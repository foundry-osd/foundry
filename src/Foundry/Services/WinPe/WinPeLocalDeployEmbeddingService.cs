using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeLocalDeployEmbeddingService : IWinPeLocalDeployEmbeddingService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

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
        bool useLocalOverride = IsEnabledEnvironmentFlag(Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployEnableEnvironmentVariable));
        _logger.LogInformation(
            "Provisioning Foundry.Deploy archive into mounted WinPE image. Architecture={Architecture}, UseLocalOverride={UseLocalOverride}",
            architecture,
            useLocalOverride);

        WinPeResult<string> archiveResult = useLocalOverride
            ? await ResolveLocalDeployArchivePathAsync(architecture, workingDirectoryPath, cancellationToken).ConfigureAwait(false)
            : await DownloadReleaseArchiveAsync(architecture, workingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!archiveResult.IsSuccess)
        {
            _logger.LogWarning("Failed to resolve Foundry.Deploy archive path. Code={ErrorCode}, Message={ErrorMessage}",
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

    private async Task<WinPeResult<string>> DownloadReleaseArchiveAsync(
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string runtimeIdentifier = architecture.ToDotnetRuntimeIdentifier();
        string assetName = architecture switch
        {
            WinPeArchitecture.X64 => "Foundry.Deploy-win-x64.zip",
            WinPeArchitecture.Arm64 => "Foundry.Deploy-win-arm64.zip",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Unsupported WinPE architecture.")
        };

        string downloadRoot = Path.Combine(workingDirectoryPath, "FoundryDeployRelease");
        Directory.CreateDirectory(downloadRoot);
        string archivePath = Path.Combine(downloadRoot, assetName);

        try
        {
            ReleaseAssetInfo asset = await ResolveReleaseAssetAsync(assetName, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Downloading Foundry.Deploy release archive. RuntimeIdentifier={RuntimeIdentifier}, AssetName={AssetName}, DownloadUrl={DownloadUrl}",
                runtimeIdentifier,
                asset.Name,
                asset.DownloadUrl);

            await using Stream sourceStream = await HttpClient.GetStreamAsync(asset.DownloadUrl, cancellationToken).ConfigureAwait(false);
            await using FileStream destinationStream = new(archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);

            return WinPeResult<string>.Success(archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Foundry.Deploy release archive. AssetName={AssetName}", assetName);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.DownloadFailed,
                "Failed to download Foundry.Deploy release archive for boot image provisioning.",
                ex.ToString());
        }
    }

    private async Task<ReleaseAssetInfo> ResolveReleaseAssetAsync(string assetName, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.github.com/repos/mchave3/Foundry/releases/latest");
        using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (JsonElement asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            string? currentName = asset.GetProperty("name").GetString();
            if (!string.Equals(currentName, assetName, StringComparison.Ordinal))
            {
                continue;
            }

            string? downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                break;
            }

            return new ReleaseAssetInfo(currentName!, downloadUrl);
        }

        throw new InvalidOperationException($"Release asset '{assetName}' was not found in the latest GitHub release.");
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

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Foundry/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private sealed record ReleaseAssetInfo(string Name, string DownloadUrl);
}
