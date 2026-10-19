// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.RegularExpressions;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

internal static partial class MicrosoftUpdateCatalogSupport
{
    public static string[] BuildReleaseSearchOrder(string targetReleaseId)
    {
        var order = new List<string>();
        string normalizedTarget = string.IsNullOrWhiteSpace(targetReleaseId)
            ? string.Empty
            : targetReleaseId.Trim();

        if (OperatingSystemSupportMatrix.IsSupportedReleaseId(normalizedTarget))
        {
            order.Add(normalizedTarget);
        }

        foreach (string release in OperatingSystemSupportMatrix.ReleaseSearchOrder)
        {
            if (!order.Contains(release, StringComparer.OrdinalIgnoreCase))
            {
                order.Add(release);
            }
        }

        return order.ToArray();
    }

    public static string? TryExtractDriverSearchHardwareId(PnpDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);

        string? match = TryExtractDriverSearchHardwareId(device.DeviceId);
        if (!string.IsNullOrWhiteSpace(match))
        {
            return match;
        }

        foreach (string hardwareId in device.HardwareIds)
        {
            match = TryExtractDriverSearchHardwareId(hardwareId);
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    public static string? TryExtractDriverSearchHardwareId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Match match = DriverHardwareIdRegex().Match(value);
        if (!match.Success)
        {
            match = SurfaceHardwareIdRegex().Match(value);
        }

        return match.Success ? match.Value : null;
    }

    public static string BuildSearchQuery(params string[] segments)
    {
        return string.Join(
            "+",
            segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => segment.Trim()));
    }

    public static string NormalizeArchitecture(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "x64" => "x64",
            "arm64" => "arm64",
            "aarch64" => "arm64",
            "x86" => "x86",
            _ => normalized
        };
    }

    /// <summary>
    /// Returns the first search result whose title names no architecture that conflicts with
    /// <paramref name="targetArchitecture"/>, or the first result when every title conflicts.
    /// </summary>
    /// <remarks>
    /// The catalog ordering (newest first) is kept. The title is only a hint; <see cref="SelectPreferredCab"/>
    /// remains the authority that rejects payloads built for another architecture.
    /// </remarks>
    public static MicrosoftUpdateCatalogUpdate? SelectPreferredUpdate(IReadOnlyList<MicrosoftUpdateCatalogUpdate> updates, string targetArchitecture)
    {
        string normalizedArchitecture = NormalizeArchitecture(targetArchitecture);
        return updates.FirstOrDefault(update => !HasConflictingArchitecture(update.Title.ToLowerInvariant(), normalizedArchitecture))
            ?? updates.FirstOrDefault();
    }

    /// <summary>
    /// Selects the CAB payload to download for <paramref name="targetArchitecture"/>: an exact architecture
    /// match first, then a CAB that names no conflicting architecture, or the first CAB when the target is empty.
    /// </summary>
    /// <remarks>
    /// A CAB built for another architecture is never selected because DISM rejects its INF files;
    /// <see langword="null"/> is returned instead so the caller skips the update.
    /// </remarks>
    public static MicrosoftUpdateCatalogDownload? SelectPreferredCab(IReadOnlyList<MicrosoftUpdateCatalogDownload> downloads, string targetArchitecture)
    {
        if (downloads.Count == 0)
        {
            return null;
        }

        string normalizedArchitecture = NormalizeArchitecture(targetArchitecture);
        MicrosoftUpdateCatalogDownload[] cabDownloads = downloads
            .Where(download => IsCabUrl(download.DownloadUrl))
            .GroupBy(download => download.DownloadUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (cabDownloads.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(normalizedArchitecture))
        {
            return cabDownloads[0];
        }

        MicrosoftUpdateCatalogDownload? exactMatch = cabDownloads.FirstOrDefault(download => MatchesArchitecture(download, normalizedArchitecture));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        return cabDownloads.FirstOrDefault(download => !HasConflictingArchitecture(ResolveComparableName(download), normalizedArchitecture));
    }

    /// <summary>
    /// Returns whether <paramref name="downloads"/> offers CAB payloads but every one of them targets an
    /// architecture other than <paramref name="targetArchitecture"/>.
    /// </summary>
    public static bool HasOnlyConflictingCabs(IReadOnlyList<MicrosoftUpdateCatalogDownload> downloads, string targetArchitecture)
    {
        return downloads.Any(download => IsCabUrl(download.DownloadUrl)) &&
               SelectPreferredCab(downloads, targetArchitecture) is null;
    }

    public static string ResolvePreferredHash(MicrosoftUpdateCatalogDownload download)
    {
        ArgumentNullException.ThrowIfNull(download);

        return !string.IsNullOrWhiteSpace(download.Sha256)
            ? download.Sha256
            : download.Sha1;
    }

    public static string ResolveFileNameFromUrl(string downloadUrl)
    {
        if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri))
        {
            string fileName = Path.GetFileName(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return SanitizePathSegment(fileName);
            }
        }

        return $"catalog-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.cab";
    }

    public static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "catalog";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return sanitized.Trim().TrimEnd('.');
    }

    private static bool IsCabUrl(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return Path.GetExtension(uri.AbsolutePath).Equals(".cab", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesArchitecture(MicrosoftUpdateCatalogDownload download, string targetArchitecture)
    {
        string fileName = ResolveComparableName(download);
        return targetArchitecture switch
        {
            "x64" => fileName.Contains("x64", StringComparison.Ordinal) || fileName.Contains("amd64", StringComparison.Ordinal),
            "arm64" => fileName.Contains("arm64", StringComparison.Ordinal),
            "x86" => fileName.Contains("x86", StringComparison.Ordinal) || fileName.Contains("32", StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool HasConflictingArchitecture(string comparableName, string targetArchitecture)
    {
        return targetArchitecture switch
        {
            "x64" => comparableName.Contains("arm64", StringComparison.Ordinal) || comparableName.Contains("x86", StringComparison.Ordinal),
            "arm64" => comparableName.Contains("x64", StringComparison.Ordinal) ||
                       comparableName.Contains("amd64", StringComparison.Ordinal) ||
                       comparableName.Contains("x86", StringComparison.Ordinal),
            "x86" => comparableName.Contains("arm64", StringComparison.Ordinal) ||
                     comparableName.Contains("x64", StringComparison.Ordinal) ||
                     comparableName.Contains("amd64", StringComparison.Ordinal),
            _ => false
        };
    }

    private static string ResolveComparableName(MicrosoftUpdateCatalogDownload download)
    {
        string fileName = string.IsNullOrWhiteSpace(download.FileName)
            ? ResolveFileNameFromUrl(download.DownloadUrl)
            : download.FileName;

        return $"{fileName} {download.Architectures}".ToLowerInvariant();
    }

    [GeneratedRegex("v[ei][dn]_([0-9a-f]){4}&[pd][ie][dv]_([0-9a-f]){4}", RegexOptions.IgnoreCase)]
    private static partial Regex DriverHardwareIdRegex();

    [GeneratedRegex("mshw0[0-1]([0-9]){2}", RegexOptions.IgnoreCase)]
    private static partial Regex SurfaceHardwareIdRegex();
}
