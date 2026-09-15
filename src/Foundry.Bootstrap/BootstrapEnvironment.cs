// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.Utilities.Diagnostics;

namespace Foundry.Bootstrap;

/// <summary>Describes a readable volume without coupling selection to drive enumeration.</summary>
internal sealed record BootstrapVolume(string Root, bool IsReady, string Label);

/// <summary>Resolves media layout once so every child inherits the same boot context.</summary>
internal static class BootstrapEnvironment
{
    internal static string ResolveRuntimeIdentifier(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        _ => throw new PlatformNotSupportedException("Foundry Bootstrap requires x64 or ARM64 Windows PE.")
    };

    internal static BootstrapContext Create(string winPeRoot, string sessionId, Architecture architecture,
        IEnumerable<BootstrapVolume> volumes, Func<string, string?> readProvisioningSource)
    {
        string runtimeIdentifier = ResolveRuntimeIdentifier(architecture);
        BootstrapVolume? cache = ResolveCacheVolume(volumes);
        return new BootstrapContext(winPeRoot,
            Path.Combine(cache?.Root ?? winPeRoot, "Runtime"), runtimeIdentifier, sessionId,
            cache is null ? null : Path.Combine(cache.Root, "Logs", sessionId),
            IsDebug("connect"), IsDebug("deploy"));

        bool IsDebug(string application)
        {
            try
            {
                string? source = readProvisioningSource(Path.Combine(winPeRoot, "Config", $"foundry.{application}.provisioning-source.txt"));
                return string.Equals(source?.Trim(), "debug", StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    /// <summary>Rejects ambiguous cache discovery before any runtime or diagnostic data uses a volume.</summary>
    internal static BootstrapVolume? ResolveCacheVolume(IEnumerable<BootstrapVolume> volumes)
    {
        BootstrapVolume[] candidates = volumes.Where(volume => volume.IsReady &&
            string.Equals(volume.Label, "Foundry Cache", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (candidates.Length > 1)
            throw new InvalidDataException("Multiple Foundry Cache volumes are connected. Disconnect the other deployment media and restart.");
        return candidates.SingleOrDefault();
    }

    internal static IEnumerable<BootstrapVolume> EnumerateVolumes()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            BootstrapVolume? volume = null;
            try
            {
                if (drive.IsReady)
                {
                    volume = new BootstrapVolume(drive.RootDirectory.FullName, true, drive.VolumeLabel);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (volume is not null)
            {
                yield return volume;
            }
        }
    }
}
