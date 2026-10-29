// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Media;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Configuration;
using Foundry.Utilities.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Services.Images;

/// <summary>Reports independently usable source entries and an actionable media-discovery diagnostic.</summary>
public sealed record CustomImageCatalogResult(IReadOnlyList<CustomImageAsset> Images, string ErrorKey = "");

/// <summary>
/// Discovers configured manifests and top-level manual WIMs only on matching Foundry media, then under the connected
/// network cache share content root, which mirrors the media layout.
/// </summary>
public sealed class CustomImageCatalogService(
    IVolumeDiscovery? volumes = null,
    INetworkCacheSessionAccessor? networkCache = null,
    ILogger<CustomImageCatalogService>? logger = null)
{
    private readonly IVolumeDiscovery _volumes = volumes ?? new WindowsVolumeDiscovery();
    private readonly INetworkCacheSessionAccessor? _networkCache = networkCache;
    private readonly ILogger _logger = logger ?? NullLogger<CustomImageCatalogService>.Instance;

    public async Task<CustomImageCatalogResult> DiscoverAsync(DeployCustomImagesSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsEnabled) return new([]);
        if (!CustomImageMediaManifestReader.HasValidBinding(settings) || !Enum.IsDefined(settings.DefaultSource) || settings.DefaultImageIndex is < 1)
            return new([], "CustomImages.InvalidManifest");
        var images = new List<CustomImageAsset>();
        string error = string.Empty;
        foreach (VolumeInfo volume in _volumes.GetVolumes().Where(volume => volume.IsReady &&
            (volume.DriveType == DriveType.CDRom || FoundryMediaVolumeLabels.IsUsbContentVolume(volume.VolumeLabel))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                bool isOptical = volume.DriveType == DriveType.CDRom;
                images.AddRange(await ReadRootAsync(volume.RootPath, isOptical, includeManualImages: !isOptical, settings, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (IsInvalidContent(exception))
            {
                error = "CustomImages.InvalidManifest";
            }
        }
        // Invalid share content only matters when local media offers nothing; the local manifest is the same bound manifest.
        if (await ReadNetworkCacheAsync(settings, images, cancellationToken).ConfigureAwait(false) && images.Count == 0) error = "CustomImages.InvalidManifest";
        return new(images, images.Count == 0 && error.Length == 0 ? "CustomImages.NoImages" : error);
    }

    /// <summary>
    /// Adds the images found under the network cache content root after local media, skipping managed images that local
    /// media already offers, so local media wins. Runs off the calling thread because UNC I/O can stall. A share I/O
    /// failure is logged and treated as not found; returns whether the share holds invalid content.
    /// </summary>
    private async Task<bool> ReadNetworkCacheAsync(DeployCustomImagesSettings settings, List<CustomImageAsset> images, CancellationToken cancellationToken)
    {
        NetworkCacheSession? session = _networkCache?.Current;
        if (session is not { IsAvailable: true, ContentRoot: { } contentRoot }) return false;
        try
        {
            IReadOnlyList<CustomImageAsset> shared = await Task.Run(
                () => ReadRootAsync(contentRoot, isOptical: false, includeManualImages: false, settings, cancellationToken), cancellationToken).ConfigureAwait(false);
            var localIds = images.Select(image => image.Id).ToHashSet(StringComparer.Ordinal);
            CustomImageAsset[] added = shared.Where(image => !localIds.Contains(image.Id)).ToArray();
            images.AddRange(added);
            if (added.Length > 0)
                _logger.LogInformation("Custom images found on the network cache share. ContentRoot={ContentRoot}, ImageCount={ImageCount}", contentRoot, added.Length);
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Custom images on the network cache share could not be read; treated as not found. ContentRoot={ContentRoot}, Reason={Reason}",
                contentRoot, exception.GetType().Name);
            return false;
        }
        catch (Exception exception) when (IsInvalidContent(exception))
        {
            _logger.LogWarning("Custom image manifest on the network cache share is invalid. ContentRoot={ContentRoot}, Reason={Reason}",
                contentRoot, exception.GetType().Name);
            return true;
        }
    }

    /// <summary>
    /// Reads the bound manifest under one media root and, when <paramref name="includeManualImages"/> is set, the manual WIMs
    /// beside it; returns nothing when the root has no manifest. A network cache share never offers manual WIMs, because anyone
    /// who can write to the share could plant one for every device of its scope.
    /// </summary>
    private static async Task<IReadOnlyList<CustomImageAsset>> ReadRootAsync(string rootPath, bool isOptical, bool includeManualImages,
        DeployCustomImagesSettings settings,
        CancellationToken cancellationToken)
    {
        CustomImageMediaManifest? manifest = await CustomImageMediaManifestReader.ReadAsync(rootPath, settings, cancellationToken).ConfigureAwait(false);
        if (manifest is null) return [];
        var rootImages = new List<CustomImageAsset>();
        foreach (CustomImageMediaEntry entry in manifest.Images)
        {
            rootImages.Add(new CustomImageAsset
            {
                Id = entry.Reference.Id,
                DisplayName = entry.Reference.DisplayName,
                ImagePath = CustomImageMediaManifestReader.ResolveContainedPath(rootPath, entry.RelativePath),
                VolumeRoot = rootPath,
                ExpectedLength = entry.Reference.Length,
                ExpectedHash = entry.Reference.ContentHash,
                RecordedIndexes = entry.Reference.Indexes,
                IsOptical = isOptical
            });
        }
        if (includeManualImages)
        {
            int manualCount = 0;
            foreach (string path in Directory.EnumerateFiles(Path.Combine(rootPath, CustomImageMediaPaths.RelativeRoot), "*.wim", SearchOption.TopDirectoryOnly).Take(257))
            {
                if (++manualCount > 256) throw new InvalidDataException();
                try { CustomImageSourceLease.EnsureRegularPath(path); }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
                rootImages.Add(new CustomImageAsset { Id = "manual:" + Path.GetFullPath(path), DisplayName = Path.GetFileNameWithoutExtension(path), ImagePath = path, VolumeRoot = rootPath });
            }
        }
        return rootImages;
    }

    private static bool IsInvalidContent(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException;
}
