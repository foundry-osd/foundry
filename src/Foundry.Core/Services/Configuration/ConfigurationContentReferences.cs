// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Lists the library content hashes a configuration references, per library.</summary>
/// <remarks>
/// A reference counts whether or not its entry is included or its section is enabled, because the entry still
/// owns its library content. Hash sets are case-insensitive.
/// </remarks>
public static class ConfigurationContentReferences
{
    /// <summary>Gets the package hashes of every post-installation action that has a package.</summary>
    public static IReadOnlySet<string> PostInstallationPackageHashes(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ToSet(configuration.PreOobe.Actions.Select(action => action.Package?.ContentHash));
    }

    /// <summary>Gets the package hashes of every custom driver set, included or not.</summary>
    public static IReadOnlySet<string> DriverSetHashes(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ToSet(configuration.CustomDrivers.Sets.Select(set => set.Package.ContentHash));
    }

    /// <summary>Gets the content hashes of every custom image entry.</summary>
    public static IReadOnlySet<string> CustomImageHashes(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ToSet(configuration.CustomImages.Images.Select(image => image.ContentHash));
    }

    private static HashSet<string> ToSet(IEnumerable<string?> hashes) =>
        new(hashes.Where(hash => !string.IsNullOrWhiteSpace(hash))!, StringComparer.OrdinalIgnoreCase);
}
