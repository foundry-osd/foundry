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
            // The id comes from the manifest content, so the same images always give the same manifest bytes and id.
            var unnamed = new CustomImageMediaManifest { Images = entries };
            string id = MediaManifestId.From(JsonSerializer.SerializeToUtf8Bytes(unnamed, ConfigurationJsonDefaults.SerializerOptions));
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(unnamed with { ManifestId = id }, ConfigurationJsonDefaults.SerializerOptions);
            return new WinPeCustomImageMediaLease(id, manifest, files.Values.ToArray(), leases);
        }
        catch
        {
            foreach (IDisposable lease in leases.AsEnumerable().Reverse()) lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds the prepared manifest identity to <c>deploy.customImages</c> of the WinPE configuration without changing runtime
    /// defaults, protected values or any other section of the document.
    /// </summary>
    public static string BindConfiguration(WinPeCustomImageMediaLease package, string winPeConfigurationJson)
    {
        package.ThrowIfDisposed();
        string bound = WinPeConfigurationJson.UpdateDeploy(winPeConfigurationJson, deploy => deploy with
        {
            CustomImages = deploy.CustomImages with { ManifestId = package.ManifestId, ManifestHash = package.ManifestHash }
        }) ?? throw new InvalidDataException("The deployment configuration is unavailable.");
        ValidateConfigurationBinding(package, bound);
        return bound;
    }

    /// <summary>Rejects stale or missing manifest bindings in the WinPE configuration before publishing images for it.</summary>
    public static void ValidateConfigurationBinding(WinPeCustomImageMediaLease package, string winPeConfigurationJson)
    {
        package.ThrowIfDisposed();
        DeployCustomImagesSettings? images = WinPeConfigurationJson.ReadDeploy(winPeConfigurationJson)?.CustomImages;
        if (images is null || !images.IsEnabled || images.ManifestId != package.ManifestId ||
            !string.Equals(images.ManifestHash, package.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The deployment configuration does not reference the prepared custom image manifest.");
    }
}
