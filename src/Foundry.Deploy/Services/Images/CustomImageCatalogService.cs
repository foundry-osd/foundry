// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Images;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Configuration;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Services.Images;

/// <summary>Reports independently usable source entries and an actionable media-discovery diagnostic.</summary>
public sealed record CustomImageCatalogResult(IReadOnlyList<CustomImageAsset> Images, string ErrorKey = "");

/// <summary>Discovers configured manifests and top-level manual WIMs only on matching Foundry media.</summary>
public sealed class CustomImageCatalogService(IVolumeDiscovery? volumes = null)
{
    public const string RelativeRoot = "Foundry/Images/Custom";
    private readonly IVolumeDiscovery _volumes = volumes ?? new WindowsVolumeDiscovery();

    public async Task<CustomImageCatalogResult> DiscoverAsync(DeployCustomImagesSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.IsEnabled) return new([]);
        if (string.IsNullOrWhiteSpace(settings.ManifestId) || settings.ManifestId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            !CustomImageSettingsValidator.IsValidHash(settings.ManifestHash) || !Enum.IsDefined(settings.DefaultSource) || settings.DefaultImageIndex is < 1)
            return new([], "CustomImages.InvalidManifest");
        var images = new List<CustomImageAsset>();
        string error = string.Empty;
        foreach (VolumeInfo volume in _volumes.GetVolumes().Where(volume => volume.IsReady &&
            (volume.DriveType == DriveType.CDRom || volume.VolumeLabel == "Foundry Cache")))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string manifestPath = Path.Combine(volume.RootPath, RelativeRoot, "manifests", settings.ManifestId + ".json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                CustomImageSourceLease.EnsureRegularPath(manifestPath);
                using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length is <= 0 or > 16 * 1024 * 1024) throw new InvalidDataException();
                byte[] bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(settings.ManifestHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException();
                CustomImageMediaManifest manifest = JsonSerializer.Deserialize<CustomImageMediaManifest>(bytes, ConfigurationJsonDefaults.SerializerOptions)
                    ?? throw new InvalidDataException();
                if (manifest.SchemaVersion != 1 || manifest.ManifestId != settings.ManifestId || manifest.Images is null ||
                    manifest.Images.Count > CustomImageSettingsValidator.MaximumImages ||
                    manifest.Images.Select(image => image.Reference.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Images.Count)
                    throw new InvalidDataException();
                var volumeImages = new List<CustomImageAsset>();
                foreach (CustomImageMediaEntry entry in manifest.Images)
                {
                    if (!CustomImageSettingsValidator.IsValidReference(entry.Reference) || !entry.Reference.IsIncluded || entry.SourceFiles is null)
                        throw new InvalidDataException();
                    string expectedPath = $"{RelativeRoot}/managed/{entry.Reference.ContentHash.ToLowerInvariant()}/image.wim";
                    if (!Normalize(entry.RelativePath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                    string? source = null;
                    if (entry.SourceRelativePath is not null)
                    {
                        string expectedSource = $"{RelativeRoot}/managed/{entry.Reference.ContentHash.ToLowerInvariant()}/sources/{entry.Reference.SourceBundleHash?.ToLowerInvariant()}/sxs";
                        if (entry.Reference.SourceBundleHash is null || !Normalize(entry.SourceRelativePath).Equals(expectedSource, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException();
                        source = ResolveContainedPath(volume.RootPath, entry.SourceRelativePath);
                        foreach (CustomImageSourceFile file in entry.SourceFiles)
                        {
                            if (file.Length < 0 || !CustomImageSettingsValidator.IsValidHash(file.ContentHash)) throw new InvalidDataException();
                            _ = ResolveContainedPath(source, file.RelativePath);
                        }
                    }
                    else if (entry.SourceFiles.Count != 0 || entry.Reference.SourceBundleHash is not null) throw new InvalidDataException();
                    volumeImages.Add(new CustomImageAsset
                    {
                        Id = entry.Reference.Id,
                        DisplayName = entry.Reference.DisplayName,
                        ImagePath = ResolveContainedPath(volume.RootPath, entry.RelativePath),
                        VolumeRoot = volume.RootPath,
                        ExpectedLength = entry.Reference.Length,
                        ExpectedHash = entry.Reference.ContentHash,
                        IsOptical = volume.DriveType == DriveType.CDRom,
                        SourceDirectory = source,
                        SourceFiles = entry.SourceFiles
                    });
                }
                if (volume.DriveType != DriveType.CDRom)
                {
                    int manualCount = 0;
                    foreach (string path in Directory.EnumerateFiles(Path.Combine(volume.RootPath, RelativeRoot), "*.wim", SearchOption.TopDirectoryOnly).Take(257))
                    {
                        if (++manualCount > 256) throw new InvalidDataException();
                        try { CustomImageSourceLease.EnsureRegularPath(path); }
                        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException) { continue; }
                        volumeImages.Add(new CustomImageAsset { Id = "manual:" + Path.GetFullPath(path), DisplayName = Path.GetFileNameWithoutExtension(path), ImagePath = path, VolumeRoot = volume.RootPath });
                    }
                }
                images.AddRange(volumeImages);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
            {
                error = "CustomImages.InvalidManifest";
            }
        }
        return new(images, images.Count == 0 && error.Length == 0 ? "CustomImages.NoImages" : error);
    }

    /// <summary>Rejects rooted paths, alternate streams and traversal before accessing media content.</summary>
    public static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':') ||
            Normalize(relativePath).Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException("CustomImages.InvalidManifest");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("CustomImages.InvalidManifest");
        return result;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
