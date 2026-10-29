// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>The content sections of a bound Deploy configuration that an active network cache moves into the signed index.</summary>
/// <param name="CustomImages">The custom image section with its manifest binding.</param>
/// <param name="CustomDrivers">The custom driver set section with its manifest binding.</param>
/// <param name="PreOobe">The post-installation section with its manifest binding.</param>
public sealed record NetworkCacheContentSections(
    DeployCustomImagesSettings CustomImages,
    DeployCustomDriversSettings CustomDrivers,
    DeployPreOobeSettings PreOobe);

/// <summary>Separates the boot configuration from the content sections once media preparation has bound them.</summary>
public static class NetworkCacheBootConfiguration
{
    /// <summary>
    /// Returns the configuration to write into the boot image and the bound content sections. Media preparation binds the
    /// sections to their manifests after generation, so the split happens on the bound JSON. An active network cache (enabled
    /// and carrying a publisher key) keeps the three sections out of the boot image, because Deploy reads them from the signed
    /// index on its share; any other configuration is returned unchanged.
    /// </summary>
    /// <param name="boundDeployConfigurationJson">The Deploy configuration JSON after the media services bound their manifests.</param>
    /// <exception cref="JsonException">The text is not a Deploy configuration.</exception>
    public static (string BootConfigurationJson, NetworkCacheContentSections Sections) Split(string boundDeployConfigurationJson)
    {
        ArgumentNullException.ThrowIfNull(boundDeployConfigurationJson);
        FoundryDeployConfigurationDocument document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(
            boundDeployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions)
            ?? throw new JsonException("The Deploy configuration is empty.");
        var sections = new NetworkCacheContentSections(document.CustomImages, document.CustomDrivers, document.PreOobe);
        if (!document.NetworkCache.IsEnabled || string.IsNullOrEmpty(document.NetworkCache.PublisherPublicKey))
            return (boundDeployConfigurationJson, sections);

        FoundryDeployConfigurationDocument boot = document with
        {
            CustomImages = new(),
            CustomDrivers = new(),
            PreOobe = new()
        };
        return (JsonSerializer.Serialize(boot, ConfigurationJsonDefaults.SerializerOptions), sections);
    }

    /// <summary>
    /// Returns the content sections of <paramref name="configuration"/> bound to the prepared leases, exactly as media preparation
    /// binds them, without generating the rest of the Deploy configuration: publishing to the network cache without media needs
    /// neither the session secrets nor the deployment protection that full generation requires.
    /// </summary>
    /// <param name="configuration">The configuration being published.</param>
    /// <param name="customImages">The prepared custom images, or <see langword="null"/> when the section is disabled.</param>
    /// <param name="postInstallation">The prepared post-installation packages.</param>
    /// <param name="customDrivers">The prepared custom driver sets, or <see langword="null"/> when the section is disabled.</param>
    /// <exception cref="InvalidDataException">A lease does not match the configuration.</exception>
    public static NetworkCacheContentSections BindContentSections(
        FoundryConfigurationDocument configuration,
        WinPeCustomImageMediaLease? customImages,
        WinPeContentPackageMediaLease postInstallation,
        WinPeContentPackageMediaLease? customDrivers)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(postInstallation);
        NetworkCacheContentSections sections = DeployConfigurationGenerator.MapContentSections(configuration);
        string json = JsonSerializer.Serialize(new FoundryDeployConfigurationDocument
        {
            CustomImages = sections.CustomImages,
            CustomDrivers = sections.CustomDrivers,
            PreOobe = sections.PreOobe
        }, ConfigurationJsonDefaults.SerializerOptions);
        // Same order as media preparation: images, post-installation, then driver sets.
        if (customImages is not null) json = WinPeCustomImageMediaService.BindConfiguration(customImages, json);
        json = WinPePreOobeMediaService.BindConfiguration(postInstallation, json);
        if (customDrivers is not null) json = WinPeCustomDriverSetMediaService.BindConfiguration(customDrivers, json);
        return Split(json).Sections;
    }
}
