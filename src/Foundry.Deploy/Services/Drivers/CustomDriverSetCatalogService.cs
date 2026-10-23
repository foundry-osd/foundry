// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.Packages;
using Foundry.Deploy.Models;
using Foundry.Utilities.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Services.Drivers;

/// <summary>Outcome of looking for the bound custom driver sets on the boot media.</summary>
public enum CustomDriverSetCatalogStatus
{
    /// <summary>The feature is off, unbound, or no volume holds the media manifest (for example PXE boot).</summary>
    Unavailable,

    /// <summary>The media manifest or the configured sets do not match what the media holds.</summary>
    Invalid,

    /// <summary>Every configured set was found and matches the verified media manifest.</summary>
    Available
}

/// <summary>The discovery outcome and, when available, the sets in configuration order.</summary>
/// <param name="Status">Discovery outcome.</param>
/// <param name="Sets">Verified sets in configuration order; empty unless <see cref="CustomDriverSetCatalogStatus.Available"/>.</param>
public sealed record CustomDriverSetCatalog(CustomDriverSetCatalogStatus Status, IReadOnlyList<CustomDriverSetOption> Sets);

/// <summary>Finds and verifies the bound driver-set media manifest on ready optical volumes and Foundry USB drives, with or without cache.</summary>
public sealed class CustomDriverSetCatalogService(IVolumeDiscovery? volumes = null, ILogger<CustomDriverSetCatalogService>? logger = null)
{
    private static readonly CustomDriverSetCatalog UnavailableCatalog = new(CustomDriverSetCatalogStatus.Unavailable, []);
    private readonly IVolumeDiscovery _volumes = volumes ?? new WindowsVolumeDiscovery();
    private readonly ILogger _logger = logger ?? NullLogger<CustomDriverSetCatalogService>.Instance;
    private readonly ContentPackageMediaReader _reader = new(ContentPackageKind.Drivers);

    /// <summary>Verifies the media manifest bound by <paramref name="settings"/> and matches every configured set against it.</summary>
    public async Task<CustomDriverSetCatalog> DiscoverAsync(DeployCustomDriversSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsEnabled || string.IsNullOrWhiteSpace(settings.ManifestId) || string.IsNullOrWhiteSpace(settings.ManifestHash))
            return Unavailable("the section is disabled or not bound to a media manifest");

        string[] roots = _volumes.GetVolumes()
            .Where(volume => volume.IsReady && (volume.DriveType == DriveType.CDRom || FoundryMediaVolumeLabels.IsUsbContentVolume(volume.VolumeLabel)))
            .Select(volume => volume.RootPath)
            .ToArray();
        ContentPackageMediaReadResult read;
        try { read = await _reader.ReadAsync(roots, settings.ManifestId, settings.ManifestHash, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return Invalid("media manifest could not be read"); }
        switch (read.Status)
        {
            case ContentPackageMediaStatus.NotFound:
                return Unavailable("no ready volume holds the media manifest");
            case ContentPackageMediaStatus.Found:
                break;
            default:
                return Invalid($"media manifest check returned {read.Status}");
        }

        var sets = new List<CustomDriverSetOption>(settings.Sets.Count);
        foreach (DeployCustomDriverSet set in settings.Sets)
        {
            ContentPackageMediaPackage[] matches = read.Manifest!.Packages
                .Where(package => package.ContentHash.Equals(set.ContentHash, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1) return Invalid($"a configured set appears {matches.Length} times in the media manifest");
            ContentPackageManifest manifest = matches[0].Manifest;
            if (manifest.Files.Count != set.FileCount || manifest.Files.Sum(file => file.Length) != set.Length)
                return Invalid("a configured set differs from the media manifest in length or file count");
            sets.Add(new CustomDriverSetOption
            {
                Id = set.Id,
                DisplayName = set.DisplayName,
                ContentHash = set.ContentHash,
                Length = set.Length,
                FileCount = set.FileCount,
                InfCount = CustomDriverSetLibrary.CountInfFiles(manifest),
                MediaRoot = read.Root!,
                Manifest = manifest
            });
        }
        return Finish(new(CustomDriverSetCatalogStatus.Available, sets));
    }

    private CustomDriverSetCatalog Unavailable(string reason)
    {
        _logger.LogInformation("Custom driver set discovery finished: status {Status}, 0 sets ({Reason})", CustomDriverSetCatalogStatus.Unavailable, reason);
        return UnavailableCatalog;
    }

    private CustomDriverSetCatalog Invalid(string reason)
    {
        _logger.LogWarning("Custom driver sets on the media are invalid: {Reason}", reason);
        return Finish(new(CustomDriverSetCatalogStatus.Invalid, []));
    }

    private CustomDriverSetCatalog Finish(CustomDriverSetCatalog catalog)
    {
        _logger.LogInformation("Custom driver set discovery finished: status {Status}, {SetCount} sets", catalog.Status, catalog.Sets.Count);
        return catalog;
    }
}
