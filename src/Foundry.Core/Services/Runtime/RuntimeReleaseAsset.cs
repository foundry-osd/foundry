// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Foundry.Core.Services.Runtime;

/// <summary>Identifies an authenticated runtime archive published in a GitHub release.</summary>
/// <param name="DownloadUrl">The HTTPS archive download address.</param>
/// <param name="Sha256">The expected archive SHA256 digest.</param>
public sealed record RuntimeReleaseAsset(string DownloadUrl, string Sha256)
{
    /// <summary>Selects one runtime archive and requires its trusted digest and secure download address.</summary>
    /// <param name="release">The GitHub release metadata.</param>
    /// <param name="applicationName">The Connect, Deploy, or PostInstall application name.</param>
    /// <param name="runtimeIdentifier">The target Windows runtime identifier.</param>
    /// <returns>The validated runtime release asset.</returns>
    /// <exception cref="InvalidDataException">The identity or release asset is missing, ambiguous, or invalid.</exception>
    public static RuntimeReleaseAsset Parse(JsonElement release, string applicationName, string runtimeIdentifier)
    {
        _ = RuntimePayloadTrust.GetBaselineArchivePath(string.Empty, applicationName, runtimeIdentifier);
        string assetName = $"{applicationName}-{runtimeIdentifier}.zip";
        try
        {
            JsonElement[] assets = release.GetProperty("assets").EnumerateArray()
                .Where(item => string.Equals(item.GetProperty("name").GetString(), assetName, StringComparison.Ordinal)).ToArray();
            if (assets.Length != 1)
            {
                throw new InvalidDataException($"Release must contain exactly one '{assetName}' asset.");
            }

            JsonElement asset = assets[0];
            string? digest = asset.TryGetProperty("digest", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            string hash = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..].Trim() : "";
            if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("A valid trusted archive SHA256 digest is required.");
            }

            string? downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidDataException("Runtime release asset must use HTTPS.");
            }

            return new RuntimeReleaseAsset(downloadUrl!, hash.ToLowerInvariant());
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidDataException("Runtime release metadata is malformed.", exception);
        }
    }
}
