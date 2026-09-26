// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;

namespace Foundry.Core.Services.WinPe;

public sealed partial class WinPeCustomImageMediaService
{
    /// <summary>Acquires included library content and freezes an exact manifest before boot configuration generation.</summary>
    public async Task<WinPeCustomImageMediaLease> PrepareAsync(CustomImageLibraryService library,
        CustomImagesSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        CustomImageSettingsValidator.ThrowIfInvalid(settings, requireIncludedImage: true);
        var leases = new List<IDisposable>();
        try
        {
            var entries = new List<CustomImageMediaEntry>();
            var files = new Dictionary<string, WinPeCustomImageMediaFile>(StringComparer.OrdinalIgnoreCase);
            foreach (CustomImageReference reference in settings.Images.Where(image => settings.IsEnabled && image.IsIncluded))
            {
                CustomImageSourceLease lease = await library.AcquireAsync(reference, cancellationToken).ConfigureAwait(false);
                leases.Add(lease);
                string imageRoot = Path.Combine(CustomImageMediaPaths.RelativeRoot, reference.ContentHash.ToLowerInvariant());
                string imagePath = Path.Combine(imageRoot, "image.wim");
                files.TryAdd(imagePath, new(lease.ImagePath, imagePath, reference.Length, reference.ContentHash));
                entries.Add(new CustomImageMediaEntry
                {
                    Reference = lease.Reference,
                    RelativePath = imagePath
                });
            }
            string id = Guid.NewGuid().ToString("N");
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new CustomImageMediaManifest { ManifestId = id, Images = entries },
                ConfigurationJsonDefaults.SerializerOptions);
            return new WinPeCustomImageMediaLease(id, manifest, files.Values.ToArray(), leases);
        }
        catch
        {
            foreach (IDisposable lease in leases.AsEnumerable().Reverse()) lease.Dispose();
            throw;
        }
    }

    /// <summary>Adds the prepared manifest identity without changing runtime defaults or protected configuration values.</summary>
    public static string BindConfiguration(WinPeCustomImageMediaLease package, string deployConfigurationJson)
    {
        package.ThrowIfDisposed();
        FoundryDeployConfigurationDocument configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
            deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions)
            ?? throw new InvalidDataException("The deployment configuration is unavailable.");
        configuration = configuration with
        {
            CustomImages = configuration.CustomImages with { ManifestId = package.ManifestId, ManifestHash = package.ManifestHash }
        };
        string bound = JsonSerializer.Serialize(configuration, ConfigurationJsonDefaults.SerializerOptions);
        ValidateConfigurationBinding(package, bound);
        return bound;
    }

    /// <summary>Rejects stale or missing manifest bindings before publishing images for a boot configuration.</summary>
    public static void ValidateConfigurationBinding(WinPeCustomImageMediaLease package, string deployConfigurationJson)
    {
        package.ThrowIfDisposed();
        FoundryDeployConfigurationDocument? configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
            deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions);
        DeployCustomImagesSettings? images = configuration?.CustomImages;
        if (images is null || !images.IsEnabled || images.ManifestId != package.ManifestId ||
            !string.Equals(images.ManifestHash, package.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The deployment configuration does not reference the prepared custom image manifest.");
    }
}
