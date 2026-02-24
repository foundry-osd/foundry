using Foundry.Deploy.Models;
using System.IO;

namespace Foundry.Deploy.Services.Cache;

public sealed class CacheLocatorService : ICacheLocatorService
{
    private const string IsoRuntimeRoot = @"X:\Foundry\Runtime";
    private const string UsbFallbackRuntimeRoot = @"X:\Foundry\Runtime";
    private const string CacheVolumeLabel = "Foundry Cache";
    private const string CacheMarkerFolderName = "Foundry Cache";
    private const string RuntimeFolderName = "Runtime";

    public Task<CacheResolution> ResolveAsync(
        DeploymentMode mode,
        string? preferredRootPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedPreferred = NormalizePath(preferredRootPath);

        if (mode == DeploymentMode.Iso)
        {
            string isoRoot = string.IsNullOrWhiteSpace(normalizedPreferred)
                ? IsoRuntimeRoot
                : normalizedPreferred;

            return Task.FromResult(new CacheResolution
            {
                Mode = mode,
                RootPath = isoRoot,
                Source = string.IsNullOrWhiteSpace(normalizedPreferred) ? "ISO policy root" : "Preferred path",
                IsPersistent = IsPersistentPath(isoRoot)
            });
        }

        // USB mode:
        // 1) honor explicit preferred path when it is not the WinPE transient placeholder
        // 2) locate dedicated cache partition (label or marker folder)
        // 3) use transient WinPE runtime root as fallback.
        bool hasExplicitPreferred = !string.IsNullOrWhiteSpace(normalizedPreferred) &&
                                    !IsWinPeTransientPlaceholder(normalizedPreferred);
        if (hasExplicitPreferred)
        {
            return Task.FromResult(new CacheResolution
            {
                Mode = mode,
                RootPath = normalizedPreferred,
                Source = "Preferred path",
                IsPersistent = IsPersistentPath(normalizedPreferred)
            });
        }

        string? cacheRuntimePath = FindUsbCacheRuntimeRoot();
        if (!string.IsNullOrWhiteSpace(cacheRuntimePath))
        {
            return Task.FromResult(new CacheResolution
            {
                Mode = mode,
                RootPath = cacheRuntimePath,
                Source = "Detected USB cache partition",
                IsPersistent = true
            });
        }

        if (!string.IsNullOrWhiteSpace(normalizedPreferred))
        {
            return Task.FromResult(new CacheResolution
            {
                Mode = mode,
                RootPath = normalizedPreferred,
                Source = "USB preferred transient root",
                IsPersistent = IsPersistentPath(normalizedPreferred)
            });
        }

        return Task.FromResult(new CacheResolution
        {
            Mode = mode,
            RootPath = UsbFallbackRuntimeRoot,
            Source = "USB fallback in WinPE temp",
            IsPersistent = false
        });
    }

    private static string? FindUsbCacheRuntimeRoot()
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
                // Ignore drives that cannot expose label.
            }

            string markerPath = Path.Combine(drive.RootDirectory.FullName, CacheMarkerFolderName);
            if (Directory.Exists(markerPath))
            {
                return Path.Combine(drive.RootDirectory.FullName, RuntimeFolderName);
            }
        }

        return null;
    }

    private static string NormalizePath(string? path)
    {
        return path?.Trim() ?? string.Empty;
    }

    private static bool IsWinPeTransientPlaceholder(string path)
    {
        return path.Equals(@"X:\Foundry\Runtime", StringComparison.OrdinalIgnoreCase) ||
               path.Equals(@"X:\Foundry", StringComparison.OrdinalIgnoreCase);
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
