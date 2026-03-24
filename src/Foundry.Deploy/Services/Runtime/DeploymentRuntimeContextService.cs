using System.IO;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Runtime;

public sealed class DeploymentRuntimeContextService : IDeploymentRuntimeContextService
{
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";
    private const string CacheVolumeLabel = "Foundry Cache";
    private const string RuntimeFolderName = "Runtime";

    public DeploymentRuntimeContext Resolve()
    {
        if (TryResolveDeploymentModeFromEnvironment(out DeploymentMode modeFromEnvironment))
        {
            string? usbRoot = modeFromEnvironment == DeploymentMode.Usb
                ? TryGetUsbCacheRuntimeRoot()
                : null;
            return new DeploymentRuntimeContext(modeFromEnvironment, usbRoot);
        }

        string? detectedUsbRoot = TryGetUsbCacheRuntimeRoot();
        return string.IsNullOrWhiteSpace(detectedUsbRoot)
            ? new DeploymentRuntimeContext(DeploymentMode.Iso, null)
            : new DeploymentRuntimeContext(DeploymentMode.Usb, detectedUsbRoot);
    }

    private static bool TryResolveDeploymentModeFromEnvironment(out DeploymentMode mode)
    {
        string? raw = Environment.GetEnvironmentVariable(DeploymentModeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            mode = default;
            return false;
        }

        string normalized = raw.Trim().ToLowerInvariant();
        mode = normalized switch
        {
            "usb" => DeploymentMode.Usb,
            "iso" => DeploymentMode.Iso,
            _ => default
        };

        return normalized is "usb" or "iso";
    }

    private static string? TryGetUsbCacheRuntimeRoot()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            try
            {
                if (string.Equals(drive.VolumeLabel, CacheVolumeLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(drive.RootDirectory.FullName, RuntimeFolderName);
                }
            }
            catch
            {
                // Ignore drives that cannot expose a label.
            }

        }

        return null;
    }
}
