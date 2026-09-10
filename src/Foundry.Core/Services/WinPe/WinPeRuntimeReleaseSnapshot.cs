// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Holds immutable release metadata shared by the provisioning stages of one media build.
/// </summary>
public sealed class WinPeRuntimeReleaseSnapshot
{
    private readonly IReadOnlyDictionary<string, WinPeRuntimeReleaseAsset> _assets;

    /// <summary>
    /// Copies a release tag and its asset metadata into an isolated snapshot.
    /// </summary>
    /// <param name="tagName">The release tag returned by GitHub.</param>
    /// <param name="assets">The assets published with the release.</param>
    internal WinPeRuntimeReleaseSnapshot(string tagName, IEnumerable<WinPeRuntimeReleaseAsset> assets)
    {
        TagName = tagName;
        _assets = assets.ToDictionary(asset => asset.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the release tag returned by GitHub.
    /// </summary>
    public string TagName { get; }

    /// <summary>
    /// Resolves an asset and verifies that its download URL uses HTTPS.
    /// </summary>
    /// <param name="assetName">The architecture-specific archive name.</param>
    /// <returns>The immutable asset metadata.</returns>
    /// <exception cref="InvalidOperationException">The asset is missing or its download URL is invalid.</exception>
    internal WinPeRuntimeReleaseAsset GetAsset(string assetName)
    {
        if (!_assets.TryGetValue(assetName, out WinPeRuntimeReleaseAsset? asset) ||
            !Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"Release asset '{assetName}' is missing or has an invalid download URL.");
        }

        return asset;
    }
}

/// <summary>
/// Contains the download metadata for one release archive.
/// </summary>
/// <param name="Name">The release archive name.</param>
/// <param name="DownloadUrl">The archive download URL.</param>
/// <param name="Digest">The optional digest supplied by GitHub.</param>
internal sealed record WinPeRuntimeReleaseAsset(string Name, string DownloadUrl, string Digest);
