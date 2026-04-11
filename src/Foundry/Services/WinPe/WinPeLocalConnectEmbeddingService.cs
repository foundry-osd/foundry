using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeLocalConnectEmbeddingService : IWinPeLocalConnectEmbeddingService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly WinPeProcessRunner _processRunner;
    private readonly ILogger<WinPeLocalConnectEmbeddingService> _logger;

    public WinPeLocalConnectEmbeddingService(
        WinPeProcessRunner processRunner,
        ILogger<WinPeLocalConnectEmbeddingService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        string mediaDirectoryPath,
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string runtimeIdentifier = architecture.ToDotnetRuntimeIdentifier();
        bool useLocalOverride = IsEnabledEnvironmentFlag(Environment.GetEnvironmentVariable(WinPeDefaults.LocalConnectEnableEnvironmentVariable));
        _logger.LogInformation(
            "Provisioning Foundry.Connect runtime into WinPE outputs. Architecture={Architecture}, RuntimeIdentifier={RuntimeIdentifier}, UseLocalOverride={UseLocalOverride}",
            architecture,
            runtimeIdentifier,
            useLocalOverride);

        WinPeResult<string> archiveResult = useLocalOverride
            ? await ResolveLocalConnectArchivePathAsync(architecture, workingDirectoryPath, cancellationToken).ConfigureAwait(false)
            : await DownloadReleaseArchiveAsync(architecture, workingDirectoryPath, cancellationToken).ConfigureAwait(false);
        if (!archiveResult.IsSuccess)
        {
            _logger.LogWarning(
                "Failed to resolve Foundry.Connect archive path. Code={ErrorCode}, Message={ErrorMessage}",
                archiveResult.Error?.Code,
                archiveResult.Error?.Message);
            return WinPeResult.Failure(archiveResult.Error!);
        }

        string extractionRoot = Path.Combine(workingDirectoryPath, "FoundryConnectExtracted", runtimeIdentifier);
        string mountedImageRuntimeRoot = GetRuntimeRoot(mountedImagePath, runtimeIdentifier);
        string stagedMediaRuntimeRoot = GetRuntimeRoot(mediaDirectoryPath, runtimeIdentifier);
        string mountedImageLegacyArchivePath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedConnectArchivePathInImage);
        string stagedMediaLegacyArchivePath = Path.Combine(mediaDirectoryPath, WinPeDefaults.EmbeddedConnectArchivePathInImage);

        try
        {
            WinPeFileSystemHelper.EnsureDirectoryClean(extractionRoot);
            _logger.LogInformation(
                "Extracting Foundry.Connect runtime for WinPE provisioning. ArchivePath={ArchivePath}, ExtractionRoot={ExtractionRoot}",
                archiveResult.Value!,
                extractionRoot);
            ZipFile.ExtractToDirectory(archiveResult.Value!, extractionRoot);

            string extractedExecutablePath = Path.Combine(extractionRoot, "Foundry.Connect.exe");
            if (!File.Exists(extractedExecutablePath))
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "The Foundry.Connect runtime archive did not contain the expected executable.",
                    $"Expected executable: '{extractedExecutablePath}'.");
            }

            _logger.LogInformation(
                "Copying extracted Foundry.Connect runtime into mounted WinPE image. SourceRoot={SourceRoot}, DestinationRoot={DestinationRoot}",
                extractionRoot,
                mountedImageRuntimeRoot);
            CopyDirectory(extractionRoot, mountedImageRuntimeRoot);
            TryDeleteFile(mountedImageLegacyArchivePath);
            _logger.LogInformation(
                "Copied extracted Foundry.Connect runtime into mounted WinPE image successfully. DestinationRoot={DestinationRoot}",
                mountedImageRuntimeRoot);

            _logger.LogInformation(
                "Copying extracted Foundry.Connect runtime into staged WinPE media workspace. SourceRoot={SourceRoot}, DestinationRoot={DestinationRoot}",
                extractionRoot,
                stagedMediaRuntimeRoot);
            CopyDirectory(extractionRoot, stagedMediaRuntimeRoot);
            TryDeleteFile(stagedMediaLegacyArchivePath);
            _logger.LogInformation(
                "Copied extracted Foundry.Connect runtime into staged WinPE media workspace successfully. DestinationRoot={DestinationRoot}",
                stagedMediaRuntimeRoot);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to provision extracted Foundry.Connect runtime for WinPE. MountedImageRuntimeRoot={MountedImageRuntimeRoot}, StagedMediaRuntimeRoot={StagedMediaRuntimeRoot}",
                mountedImageRuntimeRoot,
                stagedMediaRuntimeRoot);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision the extracted Foundry.Connect runtime for WinPE.",
                ex.ToString());
        }
        finally
        {
            TryDeleteDirectory(extractionRoot);
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
            WinPeArchitecture.X64 => "Foundry.Connect-win-x64.zip",
            WinPeArchitecture.Arm64 => "Foundry.Connect-win-arm64.zip",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Unsupported WinPE architecture.")
        };

        string downloadRoot = Path.Combine(workingDirectoryPath, "FoundryConnectRelease");
        Directory.CreateDirectory(downloadRoot);
        string archivePath = Path.Combine(downloadRoot, assetName);

        try
        {
            ReleaseAssetInfo asset = await ResolveReleaseAssetAsync(assetName, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Downloading Foundry.Connect release archive. RuntimeIdentifier={RuntimeIdentifier}, AssetName={AssetName}, DownloadUrl={DownloadUrl}",
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
            _logger.LogError(ex, "Failed to download Foundry.Connect release archive. AssetName={AssetName}", assetName);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.DownloadFailed,
                "Failed to download Foundry.Connect release archive for boot image provisioning.",
                ex.ToString());
        }
    }

    private async Task<ReleaseAssetInfo> ResolveReleaseAssetAsync(string assetName, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "https://api.github.com/repos/foundry-osd/foundry/releases/latest");
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

    private async Task<WinPeResult<string>> ResolveLocalConnectArchivePathAsync(
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        CancellationToken cancellationToken)
    {
        string configuredArchivePath = (Environment.GetEnvironmentVariable(WinPeDefaults.LocalConnectArchiveEnvironmentVariable) ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(configuredArchivePath))
        {
            if (!File.Exists(configuredArchivePath))
            {
                return WinPeResult<string>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Configured local Foundry.Connect archive was not found.",
                    $"Set {WinPeDefaults.LocalConnectArchiveEnvironmentVariable} to an existing .zip file. Path: '{configuredArchivePath}'.");
            }

            _logger.LogInformation("Using configured local Foundry.Connect archive. ArchivePath={ArchivePath}", configuredArchivePath);
            return WinPeResult<string>.Success(configuredArchivePath);
        }

        string configuredProjectPath = (Environment.GetEnvironmentVariable(WinPeDefaults.LocalConnectProjectEnvironmentVariable) ?? string.Empty).Trim();
        string projectPath;
        if (!string.IsNullOrWhiteSpace(configuredProjectPath))
        {
            if (!File.Exists(configuredProjectPath))
            {
                return WinPeResult<string>.Failure(
                    WinPeErrorCodes.ValidationFailed,
                    "Configured Foundry.Connect project file was not found.",
                    $"Set {WinPeDefaults.LocalConnectProjectEnvironmentVariable} to an existing .csproj file. Path: '{configuredProjectPath}'.");
            }

            projectPath = configuredProjectPath;
            _logger.LogInformation("Using configured Foundry.Connect project path. ProjectPath={ProjectPath}", projectPath);
        }
        else if (!TryFindFoundryConnectProjectPath(out projectPath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "Unable to locate Foundry.Connect project for local WinPE embedding.",
                $"Set {WinPeDefaults.LocalConnectArchiveEnvironmentVariable} to a .zip archive or {WinPeDefaults.LocalConnectProjectEnvironmentVariable} to Foundry.Connect.csproj.");
        }
        else
        {
            _logger.LogInformation("Discovered Foundry.Connect project path automatically. ProjectPath={ProjectPath}", projectPath);
        }

        string runtimeIdentifier = architecture.ToDotnetRuntimeIdentifier();
        string localWorkspace = Path.Combine(workingDirectoryPath, "FoundryConnectLocal");
        string publishDirectory = Path.Combine(localWorkspace, "publish", runtimeIdentifier);
        string archivePath = Path.Combine(localWorkspace, $"Foundry.Connect-{runtimeIdentifier}.zip");

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
            _logger.LogError(ex, "Failed to prepare local workspace for Foundry.Connect archive generation. ArchivePath={ArchivePath}", archivePath);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to prepare local workspace for Foundry.Connect archive generation.",
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
            "Publishing local Foundry.Connect payload. RuntimeIdentifier={RuntimeIdentifier}, PublishDirectory={PublishDirectory}",
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
                "Failed to publish Foundry.Connect for local WinPE embedding.",
                publish.ToDiagnosticText());
        }

        string executablePath = Path.Combine(publishDirectory, "Foundry.Connect.exe");
        if (!File.Exists(executablePath))
        {
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Foundry.Connect publish output is incomplete.",
                $"Expected executable: '{executablePath}'.");
        }

        try
        {
            _logger.LogInformation("Creating local Foundry.Connect archive from publish output. ArchivePath={ArchivePath}", archivePath);
            ZipFile.CreateFromDirectory(publishDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Foundry.Connect archive from publish output. PublishDirectory={PublishDirectory}, ArchivePath={ArchivePath}", publishDirectory, archivePath);
            return WinPeResult<string>.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to create Foundry.Connect archive from local publish output.",
                ex.ToString());
        }

        _logger.LogInformation("Created local Foundry.Connect archive successfully. ArchivePath={ArchivePath}", archivePath);
        return WinPeResult<string>.Success(archivePath);
    }

    private static bool TryFindFoundryConnectProjectPath(out string projectPath)
    {
        foreach (string root in GetProjectDiscoveryRoots())
        {
            if (TryResolveFoundryConnectProjectPath(root, out projectPath))
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

    private static bool TryResolveFoundryConnectProjectPath(string startDirectory, out string projectPath)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Foundry.Connect", "Foundry.Connect.csproj");
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

    private static string GetRuntimeRoot(string rootPath, string runtimeIdentifier)
    {
        return Path.Combine(rootPath, "Foundry", "Runtime", "Foundry.Connect", runtimeIdentifier);
    }

    private static void CopyDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        if (!Directory.Exists(sourceDirectoryPath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: '{sourceDirectoryPath}'.");
        }

        WinPeFileSystemHelper.EnsureDirectoryClean(destinationDirectoryPath);

        foreach (string directoryPath in Directory.EnumerateDirectories(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativeDirectoryPath = Path.GetRelativePath(sourceDirectoryPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationDirectoryPath, relativeDirectoryPath));
        }

        foreach (string filePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            string relativeFilePath = Path.GetRelativePath(sourceDirectoryPath, filePath);
            string destinationFilePath = Path.Combine(destinationDirectoryPath, relativeFilePath);
            string? destinationDirectory = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(filePath, destinationFilePath, overwrite: true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
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
