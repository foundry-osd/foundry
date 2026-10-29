// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Services.Media;

/// <summary>
/// Counts the imported content that media copies next to the boot image and that a PXE start therefore does not receive.
/// </summary>
/// <param name="CustomImageCount">Included custom Windows images of the enabled Custom images page.</param>
/// <param name="CustomDriverSetCount">Included driver sets of the enabled Custom drivers page.</param>
/// <param name="PostInstallationActionCount">Enabled post-installation actions with imported files on the enabled page.</param>
public sealed record PxeMissingContent(int CustomImageCount, int CustomDriverSetCount, int PostInstallationActionCount)
{
    /// <summary>Gets a value indicating whether a PXE start misses any imported content.</summary>
    public bool HasAny => CustomImageCount > 0 || CustomDriverSetCount > 0 || PostInstallationActionCount > 0;

    /// <summary>
    /// Gets whether a PXE start receives imported content from the network cache: media creation publishes it to every
    /// share when the network cache is enabled with at least one share, and Deploy searches the share it selects.
    /// </summary>
    public static bool IsServedByNetworkCache(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return NetworkCacheContentPublisher.IsActive(configuration.NetworkCache);
    }

    /// <summary>
    /// Evaluates <paramref name="configuration"/> with the same inclusion rules that media preparation uses. Nothing is
    /// missing when <see cref="IsServedByNetworkCache"/> is true.
    /// </summary>
    public static PxeMissingContent Evaluate(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return IsServedByNetworkCache(configuration) ? new PxeMissingContent(0, 0, 0) : CountImportedContent(configuration);
    }

    /// <summary>Counts the imported content that media preparation includes.</summary>
    private static PxeMissingContent CountImportedContent(FoundryConfigurationDocument configuration)
    {
        CustomImagesSettings images = configuration.CustomImages;
        CustomDriverSetsSettings drivers = configuration.CustomDrivers;
        PreOobeSettings postInstallation = configuration.PreOobe;
        return new PxeMissingContent(
            images.IsEnabled ? images.Images.Count(image => image.IsIncluded) : 0,
            drivers.IsEnabled ? drivers.Sets.Count(set => set.IsIncluded) : 0,
            postInstallation.IsEnabled ? postInstallation.Actions.Count(action => action.IsEnabled && action.Package is not null) : 0);
    }
}
