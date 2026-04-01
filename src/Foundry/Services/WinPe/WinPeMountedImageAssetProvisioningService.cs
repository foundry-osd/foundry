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
        string? expertDeployConfigurationJson,
        IReadOnlyList<AutopilotProfileSettings> autopilotProfiles,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting mounted image asset provisioning. MountDirectoryPath={MountDirectoryPath}, Architecture={Architecture}, AutopilotProfileCount={AutopilotProfileCount}, HasExpertConfiguration={HasExpertConfiguration}",
            mountedImagePath,
            architecture,
            autopilotProfiles.Count,
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
        _logger.LogInformation("Wrote bootstrap script into mounted WinPE image. System32Path={System32Path}", system32);

        WinPeResult curlProvisioning = await ProvisionCurlInImageAsync(
            system32,
            architecture,
            cancellationToken).ConfigureAwait(false);
        if (!curlProvisioning.IsSuccess)
        {
            return curlProvisioning;
        }

        _logger.LogInformation("Provisioned curl.exe into mounted WinPE image. System32Path={System32Path}", system32);
        WinPeResult sevenZipProvisioning = ProvisionBundledSevenZipInImage(
            mountedImagePath,
            architecture);
        if (!sevenZipProvisioning.IsSuccess)
        {
            return sevenZipProvisioning;
        }

        _logger.LogInformation("Provisioned bundled 7-Zip tools into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
        WinPeResult deployConfigurationProvisioning = await ProvisionDeployConfigurationInImageAsync(
            mountedImagePath,
            expertDeployConfigurationJson,
            cancellationToken).ConfigureAwait(false);
        if (!deployConfigurationProvisioning.IsSuccess)
        {
            return deployConfigurationProvisioning;
        }

        if (!string.IsNullOrWhiteSpace(expertDeployConfigurationJson))
        {
            _logger.LogInformation("Provisioned Foundry.Deploy expert configuration into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
        }

        WinPeResult timeZoneMapProvisioning = await ProvisionTimeZoneMapInImageAsync(
            mountedImagePath,
            cancellationToken).ConfigureAwait(false);
        if (!timeZoneMapProvisioning.IsSuccess)
        {
            return timeZoneMapProvisioning;
        }

        _logger.LogInformation("Provisioned IANA to Windows timezone map into mounted WinPE image. MountDirectoryPath={MountDirectoryPath}", mountedImagePath);
        WinPeResult autopilotProvisioning = await ProvisionAutopilotProfilesInImageAsync(
            mountedImagePath,
            autopilotProfiles,
            cancellationToken).ConfigureAwait(false);
        if (!autopilotProvisioning.IsSuccess)
        {
            return autopilotProvisioning;
        }

        string startnet = Path.Combine(mountedImagePath, WinPeDefaults.DefaultStartnetPathInImage);
        string[] lines = File.Exists(startnet) ? await File.ReadAllLinesAsync(startnet, cancellationToken).ConfigureAwait(false) : ["wpeinit"];
        var merged = lines.ToList();
        if (!merged.Any(line => line.Contains(WinPeDefaults.DefaultBootstrapScriptFileName, StringComparison.OrdinalIgnoreCase)))
        {
            merged.Add(WinPeDefaults.DefaultBootstrapInvocation);
        }

        await File.WriteAllLinesAsync(startnet, merged, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated startnet.cmd in mounted WinPE image. StartnetPath={StartnetPath}", startnet);
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
                    _logger.LogInformation("Downloading curl package for Architecture={Architecture} from {PackageUrl}.", architecture, packageUrl);
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
                    _logger.LogError(ex, "Failed to download curl package for Architecture={Architecture} from {PackageUrl}.", architecture, packageUrl);
                    return WinPeResult.Failure(
                        WinPeErrorCodes.DownloadFailed,
                        "Failed to download curl package for WinPE image provisioning.",
                        $"Architecture: '{architecture}', URI: '{packageUrl}'. Error: {ex.Message}");
                }
            }
            else
            {
                _logger.LogInformation("Using cached curl package for Architecture={Architecture}. PackagePath={PackagePath}", architecture, packagePath);
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

            string? sourceCurlPath = Directory
                .EnumerateFiles(extractPath, "curl.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(sourceCurlPath))
            {
                _logger.LogWarning("Extracted curl package did not contain curl.exe. ExtractPath={ExtractPath}", extractPath);
                return WinPeResult.Failure(
                    WinPeErrorCodes.ToolNotFound,
                    "The downloaded curl package did not contain curl.exe.",
                    $"ExtractPath: '{extractPath}'.");
            }

            try
            {
                File.Copy(sourceCurlPath, destinationPath, overwrite: true);
                return WinPeResult.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy curl.exe into mounted WinPE image. SourcePath={SourcePath}, DestinationPath={DestinationPath}", sourceCurlPath, destinationPath);
                return WinPeResult.Failure(
                    WinPeErrorCodes.BuildFailed,
                    "Failed to provision curl.exe into mounted WinPE image.",
                    $"Source: '{sourceCurlPath}', Destination: '{destinationPath}'. Error: {ex.Message}");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }

            try
            {
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private WinPeResult ProvisionBundledSevenZipInImage(
        string mountedImagePath,
        WinPeArchitecture architecture)
    {
        string sourceRootPath = Path.Combine(AppContext.BaseDirectory, WinPeDefaults.BundledSevenZipRelativePath);
        if (!Directory.Exists(sourceRootPath))
        {
            _logger.LogWarning("Bundled 7-Zip assets folder not found. SourceRootPath={SourceRootPath}", sourceRootPath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip assets were not found.",
                $"Expected path: '{sourceRootPath}'.");
        }

        string runtimeFolder = architecture.ToSevenZipRuntimeFolder();
        string sourceExecutablePath = Path.Combine(sourceRootPath, runtimeFolder, "7za.exe");
        if (!File.Exists(sourceExecutablePath))
        {
            _logger.LogWarning("Bundled 7-Zip executable not found for runtime folder {RuntimeFolder}. Path={ExecutablePath}", runtimeFolder, sourceExecutablePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip executable was not found for target architecture.",
                $"Expected file: '{sourceExecutablePath}'.");
        }

        string sourceLicensePath = Path.Combine(sourceRootPath, "License.txt");
        if (!File.Exists(sourceLicensePath))
        {
            _logger.LogWarning("Bundled 7-Zip license file not found. Path={LicensePath}", sourceLicensePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip license file was not found.",
                $"Expected file: '{sourceLicensePath}'.");
        }

        string sourceReadmePath = Path.Combine(sourceRootPath, "readme.txt");
        if (!File.Exists(sourceReadmePath))
        {
            _logger.LogWarning("Bundled 7-Zip readme file not found. Path={ReadmePath}", sourceReadmePath);
            return WinPeResult.Failure(
                WinPeErrorCodes.ToolNotFound,
                "Bundled 7-Zip readme file was not found.",
                $"Expected file: '{sourceReadmePath}'.");
        }

        string destinationExecutablePath = Path.Combine(
            mountedImagePath,
            WinPeDefaults.EmbeddedSevenZipToolsPathInImage,
            runtimeFolder,
            "7za.exe");
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
            await File.WriteAllTextAsync(
                destinationPath,
                expertDeployConfigurationJson,
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
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
            await File.WriteAllTextAsync(
                destinationPath,
                WinPeDefaults.GetIanaWindowsTimeZoneMapContent(),
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
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
            _logger.LogInformation("No Autopilot profiles requested for mounted image provisioning.");
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
                await File.WriteAllTextAsync(
                    profilePath,
                    profile.JsonContent,
                    Encoding.ASCII,
                    cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Provisioned Autopilot profiles into mounted WinPE image. AutopilotRoot={AutopilotRoot}, ProfileCount={ProfileCount}",
                autopilotRoot,
                autopilotProfiles.Count);
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
}
