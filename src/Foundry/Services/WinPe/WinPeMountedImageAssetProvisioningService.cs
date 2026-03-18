using System.Text;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.WinPe;

internal sealed class WinPeMountedImageAssetProvisioningService : IWinPeMountedImageAssetProvisioningService
{
    private readonly ILogger<WinPeMountedImageAssetProvisioningService> _logger;

    public WinPeMountedImageAssetProvisioningService(ILogger<WinPeMountedImageAssetProvisioningService> logger)
    {
        _logger = logger;
    }

    public async Task<WinPeResult> ProvisionAsync(
        string mountedImagePath,
        WinPeArchitecture architecture,
        string? expertDeployConfigurationJson,
        CancellationToken cancellationToken)
    {
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

        string startnet = Path.Combine(mountedImagePath, WinPeDefaults.DefaultStartnetPathInImage);
        string[] lines = File.Exists(startnet) ? await File.ReadAllLinesAsync(startnet, cancellationToken).ConfigureAwait(false) : ["wpeinit"];
        var merged = lines.ToList();
        if (!merged.Any(line => line.Contains(WinPeDefaults.DefaultBootstrapScriptFileName, StringComparison.OrdinalIgnoreCase)))
        {
            merged.Add(WinPeDefaults.DefaultBootstrapInvocation);
        }

        await File.WriteAllLinesAsync(startnet, merged, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Updated startnet.cmd in mounted WinPE image. StartnetPath={StartnetPath}", startnet);
        return WinPeResult.Success();
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
}
