// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Single source of the network cache share layout. Final file names are immutable: one name always means the
/// same content, so writers publish through a temporary name and readers only open final names.
/// </summary>
public static class NetworkCacheLayout
{
    public const string RootFolderName = "Foundry";
    public const string DownloadsFolderName = "Downloads";
    public const string PartialSuffix = ".partial";
    public const string HashSidecarSuffix = ".sha256";

    /// <summary>Gets the download kinds shared through the network cache; any other kind bypasses the share.</summary>
    public static IReadOnlySet<string> CacheableKinds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "OperatingSystemImage",
        "OemDriverPack",
        "MicrosoftUpdateCatalogDriver",
        "MicrosoftUpdateCatalogFirmware"
    };

    /// <summary>
    /// Returns the catalog hash in lower-case hex when supplied. Hashless artifacts are keyed by the normalized
    /// source URL and the expected size, so the same URL serving a different size never reuses stale content.
    /// </summary>
    public static string DownloadKey(string sourceUrl, string? expectedHash, long? expectedSizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUrl);
        if (!string.IsNullOrWhiteSpace(expectedHash))
        {
            return expectedHash.Trim().ToLowerInvariant();
        }

        string normalizedUrl = Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out Uri? uri)
            ? uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped)
            : sourceUrl.Trim();
        string urlHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)));
        return $"u-{urlHash}-{expectedSizeBytes ?? 0}";
    }

    /// <summary>Returns <c>&lt;share&gt;\Foundry</c>, which holds downloads and imported content laid out as on media.</summary>
    public static string ContentRoot(string shareRoot) => Path.Combine(shareRoot, RootFolderName);

    /// <summary>Returns <c>&lt;share&gt;\Foundry\Downloads\&lt;kind&gt;\&lt;key&gt;</c>.</summary>
    public static string DownloadDirectory(string shareRoot, string kind, string key) =>
        Path.Combine(ContentRoot(shareRoot), DownloadsFolderName, kind, key);

    /// <summary>Returns the temporary name a session writes before the atomic rename.</summary>
    public static string PartialName(string fileName, string sessionId) => $"{fileName}.{sessionId}{PartialSuffix}";
}
