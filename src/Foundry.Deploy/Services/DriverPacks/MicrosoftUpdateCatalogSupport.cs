using System.IO;
using System.Text.RegularExpressions;
using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.DriverPacks;

internal static partial class MicrosoftUpdateCatalogSupport
{
    private static readonly string[] BaseReleaseSearchOrder =
    [
        "25H2",
        "24H2",
        "23H2",
        "22H2",
        "21H2",
        "Vibranium",
        "1903",
        "1809"
    ];

    public static string[] BuildReleaseSearchOrder(string targetReleaseId)
    {
        var order = new List<string>();
        string normalizedTarget = targetReleaseId.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedTarget))
        {
            order.Add(normalizedTarget);
        }

        foreach (string release in BaseReleaseSearchOrder)
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

    public static string? SelectPreferredCabUrl(IReadOnlyList<string> downloadUrls, string targetArchitecture)
    {
        if (downloadUrls.Count == 0)
        {
            return null;
        }

        string normalizedArchitecture = NormalizeArchitecture(targetArchitecture);
        string[] cabUrls = downloadUrls
            .Where(IsCabUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (cabUrls.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(normalizedArchitecture))
        {
            return cabUrls[0];
        }

        string? exactMatch = cabUrls.FirstOrDefault(url => MatchesArchitecture(url, normalizedArchitecture));
        if (!string.IsNullOrWhiteSpace(exactMatch))
        {
            return exactMatch;
        }

        string? compatibleFallback = cabUrls.FirstOrDefault(url => !HasConflictingArchitecture(url, normalizedArchitecture));
        return compatibleFallback ?? cabUrls[0];
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

    private static bool MatchesArchitecture(string downloadUrl, string targetArchitecture)
    {
        string fileName = ResolveFileNameFromUrl(downloadUrl).ToLowerInvariant();
        return targetArchitecture switch
        {
            "x64" => fileName.Contains("x64", StringComparison.Ordinal) || fileName.Contains("amd64", StringComparison.Ordinal),
            "arm64" => fileName.Contains("arm64", StringComparison.Ordinal),
            "x86" => fileName.Contains("x86", StringComparison.Ordinal) || fileName.Contains("32", StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool HasConflictingArchitecture(string downloadUrl, string targetArchitecture)
    {
        string fileName = ResolveFileNameFromUrl(downloadUrl).ToLowerInvariant();
        return targetArchitecture switch
        {
            "x64" => fileName.Contains("arm64", StringComparison.Ordinal) || fileName.Contains("x86", StringComparison.Ordinal),
            "arm64" => fileName.Contains("x64", StringComparison.Ordinal) ||
                       fileName.Contains("amd64", StringComparison.Ordinal) ||
                       fileName.Contains("x86", StringComparison.Ordinal),
            "x86" => fileName.Contains("arm64", StringComparison.Ordinal) ||
                     fileName.Contains("x64", StringComparison.Ordinal) ||
                     fileName.Contains("amd64", StringComparison.Ordinal),
            _ => false
        };
    }

    [GeneratedRegex("v[ei][dn]_([0-9a-f]){4}&[pd][ie][dv]_([0-9a-f]){4}", RegexOptions.IgnoreCase)]
    private static partial Regex DriverHardwareIdRegex();

    [GeneratedRegex("mshw0[0-1]([0-9]){2}", RegexOptions.IgnoreCase)]
    private static partial Regex SurfaceHardwareIdRegex();
}
