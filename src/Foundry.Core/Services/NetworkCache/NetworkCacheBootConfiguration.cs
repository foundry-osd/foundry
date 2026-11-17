// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>The content sections of a bound <c>deploy</c> node that an active network cache moves into the signed index.</summary>
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
    /// Returns the WinPE configuration to write into the boot image and the bound content sections of its <c>deploy</c> node.
    /// Media preparation binds the sections to their manifests after generation, so the split happens on the bound JSON. An
    /// active network cache (enabled and carrying a publisher key) keeps the three sections out of the boot image, because
    /// Deploy reads them from the signed index on its share; only the <c>deploy</c> node changes. Any other configuration is
    /// returned unchanged.
    /// </summary>
    /// <param name="boundConfigurationJson">The WinPE configuration JSON after the media services bound their manifests.</param>
    /// <exception cref="JsonException">The text is not a WinPE runtime configuration.</exception>
    public static (string BootConfigurationJson, NetworkCacheContentSections Sections) Split(string boundConfigurationJson)
    {
        ArgumentNullException.ThrowIfNull(boundConfigurationJson);
        DeployRuntimeSettings deploy = WinPeConfigurationJson.ReadDeploy(boundConfigurationJson)
            ?? throw new JsonException("The WinPE runtime configuration has no deploy section.");
        var sections = new NetworkCacheContentSections(deploy.CustomImages, deploy.CustomDrivers, deploy.PreOobe);
        if (!deploy.NetworkCache.IsEnabled || string.IsNullOrEmpty(deploy.NetworkCache.PublisherPublicKey))
            return (boundConfigurationJson, sections);

        string boot = WinPeConfigurationJson.UpdateDeploy(boundConfigurationJson, bound => bound with
        {
            CustomImages = new(),
            CustomDrivers = new(),
            PreOobe = new()
        })!;
        return (boot, sections);
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
        string json = WinPeConfigurationGenerator.Serialize(new FoundryWinPeConfigurationDocument
        {
            Deploy = new DeployRuntimeSettings
            {
                CustomImages = sections.CustomImages,
                CustomDrivers = sections.CustomDrivers,
                PreOobe = sections.PreOobe
            }
        });
        // Same order as media preparation: images, post-installation, then driver sets.
        if (customImages is not null) json = WinPeCustomImageMediaService.BindConfiguration(customImages, json);
        json = WinPePreOobeMediaService.BindConfiguration(postInstallation, json);
        if (customDrivers is not null) json = WinPeCustomDriverSetMediaService.BindConfiguration(customDrivers, json);
        return Split(json).Sections;
    }
}
