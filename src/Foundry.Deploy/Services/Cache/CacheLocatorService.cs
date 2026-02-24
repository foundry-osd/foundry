using Foundry.Deploy.Models;
using System.IO;

namespace Foundry.Deploy.Services.Cache;

public sealed class CacheLocatorService : ICacheLocatorService
{
    private const string IsoRoot = @"C:\Foundry\Deploy";
    private const string UsbFallbackRoot = @"X:\Windows\Temp\Foundry\Deploy";
    private const string CacheFolderName = "Foundry Cache";

    public Task<CacheResolution> ResolveAsync(
        DeploymentMode mode,
        string? preferredRootPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mode == DeploymentMode.Iso)
        {
            return Task.FromResult(new CacheResolution
            {
                Mode = mode,
                RootPath = IsoRoot,
                Source = "ISO policy root",
                IsPersistent = true
            });
        }

        // USB mode:
        // 1) honor explicit preferred path if provided
        // 2) locate dedicated cache partition (label or folder)
        // 3) fallback to WinPE temp path.
        if (!string.IsNullOrWhiteSpace(preferredRootPath))
        {
            return Task.FromResult(new CacheResolution
            {
                Mode = mode,
                RootPath = preferredRootPath.Trim(),
                Source = "Preferred path",
                IsPersistent = IsPersistentPath(preferredRootPath)
            });
        }

        string? cachePartitionPath = FindUsbCachePartitionRoot();
        if (!string.IsNullOrWhiteSpace(cachePartitionPath))
        {
            return Task.FromResult(new CacheResolution
            {
                Mode = mode,
                RootPath = cachePartitionPath,
                Source = "Detected USB cache partition",
                IsPersistent = true
            });
        }

        return Task.FromResult(new CacheResolution
        {
            Mode = mode,
            RootPath = UsbFallbackRoot,
            Source = "USB fallback in WinPE temp",
            IsPersistent = false
        });
    }

    private static string? FindUsbCachePartitionRoot()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            try
            {
                if (string.Equals(drive.VolumeLabel, CacheFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(drive.RootDirectory.FullName, CacheFolderName, "Deploy");
                }
            }
            catch
            {
                // Ignore drives that cannot expose label.
            }

            string folderPath = Path.Combine(drive.RootDirectory.FullName, CacheFolderName);
            if (Directory.Exists(folderPath))
            {
                return Path.Combine(folderPath, "Deploy");
            }
        }

        return null;
    }

    private static bool IsPersistentPath(string path)
    {
        string normalized = path.Trim();
        if (normalized.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
