using Foundry.Deploy.Models;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Cache;

public sealed class CacheLocatorService : ICacheLocatorService
{
    private const string WinPeTransientRoot = @"X:\Foundry";
    private const string WinPeTransientRuntimeRoot = @"X:\Foundry\Runtime";
    private const string CacheVolumeLabel = "Foundry Cache";
    private const string CacheMarkerFolderName = "Foundry Cache";
    private const string RuntimeFolderName = "Runtime";
    private readonly ILogger<CacheLocatorService> _logger;

    public CacheLocatorService(ILogger<CacheLocatorService> logger)
    {
        _logger = logger;
    }

    public Task<CacheResolution> ResolveAsync(
        DeploymentMode mode,
        string? preferredRootPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string preferredRoot = NormalizePath(preferredRootPath);
        _logger.LogInformation("Resolving cache strategy. Mode={Mode}, PreferredRootPath={PreferredRootPath}", mode, preferredRoot);
        CacheResolution resolution = mode switch
        {
            DeploymentMode.Iso => ResolveIso(mode, preferredRoot),
            _ => ResolveUsb(mode, preferredRoot)
        };

        _logger.LogInformation("Resolved cache strategy. RootPath={RootPath}, Source={Source}, IsPersistent={IsPersistent}",
            resolution.RootPath,
            resolution.Source,
            resolution.IsPersistent);
        return Task.FromResult(resolution);
    }

    private static CacheResolution ResolveIso(DeploymentMode mode, string preferredRoot)
    {
        bool hasPreferred = !string.IsNullOrWhiteSpace(preferredRoot);
        return CreateResolution(
            mode,
            hasPreferred ? preferredRoot : WinPeTransientRuntimeRoot,
            hasPreferred ? "Preferred path" : "ISO policy root",
            isPersistent: false);
    }

    private static CacheResolution ResolveUsb(DeploymentMode mode, string preferredRoot)
    {
        // USB mode:
        // 1) honor explicit preferred path when it is not the WinPE transient placeholder
        // 2) locate dedicated cache partition (label or marker folder)
        // 3) use transient WinPE runtime root as fallback.
        if (!string.IsNullOrWhiteSpace(preferredRoot) &&
            !IsWinPeTransientPlaceholder(preferredRoot))
        {
            return CreateResolution(mode, preferredRoot, "Preferred path", isPersistent: false);
        }

        string? cacheRuntimePath = FindUsbCacheRuntimeRoot();
        if (!string.IsNullOrWhiteSpace(cacheRuntimePath))
        {
            return CreateResolution(mode, cacheRuntimePath, "Detected USB cache partition", isPersistent: true);
        }

        if (!string.IsNullOrWhiteSpace(preferredRoot))
        {
            return CreateResolution(mode, preferredRoot, "USB preferred transient root", isPersistent: false);
        }

        return CreateResolution(mode, WinPeTransientRuntimeRoot, "USB fallback in WinPE temp", isPersistent: false);
    }

    private static CacheResolution CreateResolution(
        DeploymentMode mode,
        string rootPath,
        string source,
        bool isPersistent)
    {
        return new CacheResolution
        {
            Mode = mode,
            RootPath = rootPath,
            Source = source,
            IsPersistent = isPersistent
        };
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
        return path.Equals(WinPeTransientRuntimeRoot, StringComparison.OrdinalIgnoreCase) ||
               path.Equals(WinPeTransientRoot, StringComparison.OrdinalIgnoreCase);
    }

}
