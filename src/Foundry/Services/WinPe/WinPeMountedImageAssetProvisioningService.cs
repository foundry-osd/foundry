using System.IO.Compression;
using System.Net.Http;
using System.Text;
using Foundry.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeMountedImageAssetProvisioningService : IWinPeMountedImageAssetProvisioningService
{
    private const string CurlX64PackageUrl = "https://curl.se/windows/latest.cgi?p=win64-mingw.zip";
    private const string CurlArm64PackageUrl = "https://curl.se/windows/latest.cgi?p=win64a-mingw.zip";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private readonly ILogger<WinPeMountedImageAssetProvisioningService> _logger;

    public WinPeMountedImageAssetProvisioningService(ILogger<WinPeMountedImageAssetProvisioningService> logger)
    {
        _logger = logger;
    }

    public async Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string? foundryConnectConfigurationJson,
        IReadOnlyList<FoundryConnectProvisionedAssetFile> foundryConnectAssetFiles,
        string? expertDeployConfigurationJson,
        IReadOnlyList<AutopilotProfileSettings> autopilotProfiles,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting mounted image asset provisioning. MountDirectoryPath={MountDirectoryPath}, Architecture={Architecture}, AutopilotProfileCount={AutopilotProfileCount}, HasConnectConfiguration={HasConnectConfiguration}, ConnectAssetCount={ConnectAssetCount}, HasExpertConfiguration={HasExpertConfiguration}",
            mountedImagePath,
            architecture,
            autopilotProfiles.Count,
            !string.IsNullOrWhiteSpace(foundryConnectConfigurationJson),
            foundryConnectAssetFiles.Count,
            !string.IsNullOrWhiteSpace(expertDeployConfigurationJson));

        string system32 = Path.Combine(mountedImagePath, "Windows", "System32");
        Directory.CreateDirectory(system32);

        string bootstrapScriptContent;
        try
        {
            bootstrapScriptContent = WinPeDefaults.GetDefaultBootstrapScriptContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load embedded WinPE bootstrap script content.");
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to load embedded WinPE bootstrap script.",
                ex.ToString());
        }

        await File.WriteAllTextAsync(
            Path.Combine(system32, WinPeDefaults.DefaultBootstrapScriptFileName),
            bootstrapScriptContent,
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);

        WinPeResult curlProvisioning = await ProvisionCurlInImageAsync(system32, architecture, cancellationToken).ConfigureAwait(false);
        if (!curlProvisioning.IsSuccess)
        {
            return curlProvisioning;
        }

        WinPeResult sevenZipProvisioning = ProvisionBundledSevenZipInImage(mountedImagePath, architecture);
        if (!sevenZipProvisioning.IsSuccess)
        {
            return sevenZipProvisioning;
        }

        WinPeResult connectProvisioning = await ProvisionConnectAssetsInImageAsync(
            mountedImagePath,
            foundryConnectConfigurationJson,
            foundryConnectAssetFiles,
            cancellationToken).ConfigureAwait(false);
        if (!connectProvisioning.IsSuccess)
        {
            return connectProvisioning;
        }

        WinPeResult deployConfigurationProvisioning = await ProvisionDeployConfigurationInImageAsync(
            mountedImagePath,
            expertDeployConfigurationJson,
            cancellationToken).ConfigureAwait(false);
        if (!deployConfigurationProvisioning.IsSuccess)
        {
            return deployConfigurationProvisioning;
        }

        WinPeResult timeZoneMapProvisioning = await ProvisionTimeZoneMapInImageAsync(
            mountedImagePath,
            cancellationToken).ConfigureAwait(false);
        if (!timeZoneMapProvisioning.IsSuccess)
        {
            return timeZoneMapProvisioning;
        }

        WinPeResult autopilotProvisioning = await ProvisionAutopilotProfilesInImageAsync(
            mountedImagePath,
            autopilotProfiles,
            cancellationToken).ConfigureAwait(false);
        if (!autopilotProvisioning.IsSuccess)
        {
            return autopilotProvisioning;
        }

        string startnet = Path.Combine(mountedImagePath, WinPeDefaults.DefaultStartnetPathInImage);
        string[] lines = File.Exists(startnet)
            ? await File.ReadAllLinesAsync(startnet, cancellationToken).ConfigureAwait(false)
            : ["wpeinit"];
        List<string> merged = lines.ToList();
        if (!merged.Any(line => line.Contains(WinPeDefaults.DefaultBootstrapScriptFileName, StringComparison.OrdinalIgnoreCase)))
        {
            merged.Add(WinPeDefaults.DefaultBootstrapInvocation);
        }

        await File.WriteAllLinesAsync(startnet, merged, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Mounted image asset provisioning completed successfully. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
        return WinPeResult.Success();
    }

    private async Task<WinPeResult> ProvisionCurlInImageAsync(
        string system32Path,
        WinPeArchitecture architecture,
        CancellationToken cancellationToken)
    {
        string cacheRootPath = Path.Combine(WinPeDefaults.GetInstallerCacheDirectoryPath(), "curl");
        Directory.CreateDirectory(cacheRootPath);

        string packageUrl = architecture switch
        {
            WinPeArchitecture.X64 => CurlX64PackageUrl,
            WinPeArchitecture.Arm64 => CurlArm64PackageUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Unsupported WinPE architecture.")
        };

        string packageFileName = architecture switch
        {
            WinPeArchitecture.X64 => "curl-x64.zip",
            WinPeArchitecture.Arm64 => "curl-arm64.zip",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Unsupported WinPE architecture.")
        };

        string extractDirectoryName = architecture switch
        {
            WinPeArchitecture.X64 => "curl-x64",
            WinPeArchitecture.Arm64 => "curl-arm64",
            _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Unsupported WinPE architecture.")
        };

        string packagePath = Path.Combine(cacheRootPath, packageFileName);
        string extractPath = Path.Combine(cacheRootPath, extractDirectoryName);
        string destinationPath = Path.Combine(system32Path, "curl.exe");

        try
        {
            if (!File.Exists(packagePath))
            {
                try
                {
                    using HttpResponseMessage response = await HttpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return WinPeResult.Failure(
                            WinPeErrorCodes.DownloadFailed,
                            "Failed to download curl package for WinPE image provisioning.",
                            $"Architecture: '{architecture}', URI: '{packageUrl}', HTTP status: {(int)response.StatusCode} {response.ReasonPhrase}");
                    }

                    await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using FileStream destinationStream = new(packagePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download curl package. Architecture={Architecture}, PackageUrl={PackageUrl}", architecture, packageUrl);
                    return WinPeResult.Failure(
                        WinPeErrorCodes.DownloadFailed,
                        "Failed to download curl package for WinPE image provisioning.",
                        $"Architecture: '{architecture}', URI: '{packageUrl}'. Error: {ex.Message}");
                }
            }

            WinPeFileSystemHelper.EnsureDirectoryClean(extractPath);
            try
            {
                ZipFile.ExtractToDirectory(packagePath, extractPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract curl package. PackagePath={PackagePath}, ExtractPath={ExtractPath}", packagePath, extractPath);
                return WinPeResult.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to extract curl package for WinPE image provisioning.",
                    $"Package: '{packagePath}', ExtractPath: '{extractPath}'. Error: {ex.Message}");
            }

            string? sourceCurlPath = Directory.EnumerateFiles(extractPath, "curl.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sourceCurlPath))
            {
                return WinPeResult.Failure(
                    WinPeErrorCodes.ToolNotFound,
                    "The downloaded curl package did not contain curl.exe.",
                    $"ExtractPath: '{extractPath}'.");
            }

            File.Copy(sourceCurlPath, destinationPath, overwrite: true);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision curl.exe into mounted WinPE image. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision curl.exe into mounted WinPE image.",
                ex.ToString());
        }
        finally
        {
            TryDeleteFile(packagePath);
            TryDeleteDirectory(extractPath);
        }
    }

    private async Task<WinPeResult> ProvisionConnectAssetsInImageAsync(
        string mountedImagePath,
        string? configurationJson,
        IReadOnlyList<FoundryConnectProvisionedAssetFile> assetFiles,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            configurationJson = CreateFallbackConnectConfigurationJson();
        }

        string configurationPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedConnectConfigPathInImage);
        string? configurationDirectoryPath = Path.GetDirectoryName(configurationPath);
        if (string.IsNullOrWhiteSpace(configurationDirectoryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for Foundry.Connect configuration.",
                $"Destination file: '{configurationPath}'.");
        }

        try
        {
            Directory.CreateDirectory(configurationDirectoryPath);
            await File.WriteAllTextAsync(configurationPath, configurationJson, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            CreateNetworkAssetLayout(mountedImagePath);
            foreach (FoundryConnectProvisionedAssetFile assetFile in assetFiles)
            {
                if (string.IsNullOrWhiteSpace(assetFile.SourcePath) || !File.Exists(assetFile.SourcePath))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.ValidationFailed,
                        "A Foundry.Connect provisioning asset is missing.",
                        $"Source file: '{assetFile.SourcePath}'.");
                }

                string destinationPath = Path.Combine(mountedImagePath, assetFile.RelativeDestinationPath);
                string? destinationDirectoryPath = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
                {
                    return WinPeResult.Failure(
                        WinPeErrorCodes.InternalError,
                        "Failed to resolve destination path for a Foundry.Connect network asset.",
                        $"Destination file: '{destinationPath}'.");
                }

                Directory.CreateDirectory(destinationDirectoryPath);
                await using FileStream sourceStream = new(assetFile.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using FileStream destinationStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            }

            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Foundry.Connect configuration or assets into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Foundry.Connect configuration or network assets into mounted WinPE image.",
                ex.ToString());
        }
    }

    private WinPeResult ProvisionBundledSevenZipInImage(
        string mountedImagePath,
        WinPeArchitecture architecture)
    {
        string sourceRootPath = Path.Combine(AppContext.BaseDirectory, WinPeDefaults.BundledSevenZipRelativePath);
        if (!Directory.Exists(sourceRootPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip assets were not found.",
                $"Expected path: '{sourceRootPath}'.");
        }

        string runtimeFolder = architecture.ToSevenZipRuntimeFolder();
        string sourceExecutablePath = Path.Combine(sourceRootPath, runtimeFolder, "7za.exe");
        string sourceLicensePath = Path.Combine(sourceRootPath, "License.txt");
        string sourceReadmePath = Path.Combine(sourceRootPath, "readme.txt");

        if (!File.Exists(sourceExecutablePath) || !File.Exists(sourceLicensePath) || !File.Exists(sourceReadmePath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip assets are incomplete.",
                $"Expected files under '{sourceRootPath}' for runtime '{runtimeFolder}'.");
        }

        string destinationExecutablePath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedSevenZipToolsPathInImage, runtimeFolder, "7za.exe");
        string destinationToolsRootPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedSevenZipToolsPathInImage);
        string? destinationDirectoryPath = Path.GetDirectoryName(destinationExecutablePath);
        if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for bundled 7-Zip provisioning.",
                $"Destination file: '{destinationExecutablePath}'.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectoryPath);
            Directory.CreateDirectory(destinationToolsRootPath);
            File.Copy(sourceExecutablePath, destinationExecutablePath, overwrite: true);
            File.Copy(sourceLicensePath, Path.Combine(destinationToolsRootPath, "License.txt"), overwrite: true);
            File.Copy(sourceReadmePath, Path.Combine(destinationToolsRootPath, "readme.txt"), overwrite: true);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision bundled 7-Zip tools into mounted WinPE image. DestinationToolsRootPath={DestinationToolsRootPath}", destinationToolsRootPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision bundled 7-Zip executable into mounted WinPE image.",
                ex.ToString());
        }
    }

    private async Task<WinPeResult> ProvisionDeployConfigurationInImageAsync(
        string mountedImagePath,
        string? expertDeployConfigurationJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expertDeployConfigurationJson))
        {
            return WinPeResult.Success();
        }

        string destinationPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedDeployConfigPathInImage);
        string? destinationDirectoryPath = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for Foundry.Deploy expert configuration.",
                $"Destination file: '{destinationPath}'.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectoryPath);
            await File.WriteAllTextAsync(destinationPath, expertDeployConfigurationJson, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Foundry.Deploy expert configuration into mounted WinPE image. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Foundry.Deploy expert configuration into mounted WinPE image.",
                ex.ToString());
        }
    }

    private async Task<WinPeResult> ProvisionTimeZoneMapInImageAsync(
        string mountedImagePath,
        CancellationToken cancellationToken)
    {
        string destinationPath = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedTimeZoneMapPathInImage);
        string? destinationDirectoryPath = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
        {
            return WinPeResult.Failure(
                WinPeErrorCodes.InternalError,
                "Failed to resolve destination path for the IANA to Windows timezone map.",
                $"Destination file: '{destinationPath}'.");
        }

        try
        {
            Directory.CreateDirectory(destinationDirectoryPath);
            await File.WriteAllTextAsync(destinationPath, WinPeDefaults.GetIanaWindowsTimeZoneMapContent(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision the IANA to Windows timezone map into mounted WinPE image. DestinationPath={DestinationPath}", destinationPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision the IANA to Windows timezone map into mounted WinPE image.",
                ex.ToString());
        }
    }

    private async Task<WinPeResult> ProvisionAutopilotProfilesInImageAsync(
        string mountedImagePath,
        IReadOnlyList<AutopilotProfileSettings> autopilotProfiles,
        CancellationToken cancellationToken)
    {
        if (autopilotProfiles.Count == 0)
        {
            return WinPeResult.Success();
        }

        string autopilotRoot = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedAutopilotProfilesPathInImage);

        try
        {
            Directory.CreateDirectory(autopilotRoot);
            foreach (AutopilotProfileSettings profile in autopilotProfiles)
            {
                string profileDirectory = Path.Combine(autopilotRoot, profile.FolderName);
                Directory.CreateDirectory(profileDirectory);

                string profilePath = Path.Combine(profileDirectory, "AutopilotConfigurationFile.json");
                await File.WriteAllTextAsync(profilePath, profile.JsonContent, Encoding.ASCII, cancellationToken).ConfigureAwait(false);
            }

            return WinPeResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Autopilot profiles into mounted WinPE image. AutopilotRoot={AutopilotRoot}", autopilotRoot);
            return WinPeResult.Failure(
                WinPeErrorCodes.BuildFailed,
                "Failed to provision Autopilot profiles into mounted WinPE image.",
                ex.ToString());
        }
    }

    private static void CreateNetworkAssetLayout(string mountedImagePath)
    {
        string networkRoot = Path.Combine(mountedImagePath, WinPeDefaults.EmbeddedNetworkAssetsPathInImage);
        Directory.CreateDirectory(networkRoot);
        Directory.CreateDirectory(Path.Combine(networkRoot, "Wired", "Profiles"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Wifi", "Profiles"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Certificates"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Certificates", "Wired"));
        Directory.CreateDirectory(Path.Combine(networkRoot, "Certificates", "Wifi"));
    }

    private static string CreateFallbackConnectConfigurationJson()
    {
        return """
{
  "schemaVersion": 1,
  "capabilities": {
    "wifiProvisioned": false
  },
  "ui": {
    "windowTitle": "Foundry.Connect",
    "autoCloseDelaySeconds": 5,
    "refreshIntervalSeconds": 5
  },
  "internetProbe": {
    "probeUris": [
      "http://www.msftconnecttest.com/connecttest.txt",
      "http://www.google.com"
    ],
    "timeoutSeconds": 5
  }
}
""";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
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
}
