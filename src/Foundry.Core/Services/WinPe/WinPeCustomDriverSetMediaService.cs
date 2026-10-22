// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Custom driver set facade over <see cref="WinPeContentPackageMediaService"/>: it freezes the included sets and binds
/// the media generation to the <c>CustomDrivers</c> section of the deploy configuration.
/// </summary>
public static class WinPeCustomDriverSetMediaService
{
    private static readonly WinPeContentPackageMediaService MediaService = new(ContentPackageKind.Drivers);

    /// <summary>
    /// Freezes the included driver sets, one package per distinct content hash, before boot configuration is generated.
    /// </summary>
    /// <exception cref="InvalidDataException">The settings are invalid, disabled or include no set.</exception>
    public static async Task<WinPeContentPackageMediaLease> PrepareAsync(CustomDriverSetLibrary library, CustomDriverSetsSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        CustomDriverSetsSettingsValidator.ThrowIfInvalid(settings, requireIncludedSet: true);
        ContentPackageReference[] packages = settings.IsEnabled
            ? settings.Sets.Where(set => set.IsIncluded).Select(set => set.Package).ToArray()
            : [];
        // An enabled section without included sets is blocked by readiness checks; this keeps media from carrying an empty generation.
        if (packages.Length == 0) throw new InvalidDataException("CustomDrivers.NoIncludedSet");
        return await MediaService.PrepareAsync(library.Library, packages, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the manifest identity of <paramref name="package"/> into the <c>CustomDrivers</c> section and validates the result.</summary>
    public static string BindConfiguration(WinPeContentPackageMediaLease package, string deployConfigurationJson)
    {
        ThrowIfInvalidLease(package);
        var configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions)
            ?? throw new InvalidDataException("CustomDrivers.InvalidConfiguration");
        string bound = JsonSerializer.Serialize(configuration with
        {
            CustomDrivers = configuration.CustomDrivers with { ManifestId = package.ManifestId, ManifestHash = package.ManifestHash }
        }, ConfigurationJsonDefaults.SerializerOptions);
        ValidateConfigurationBinding(package, bound);
        return bound;
    }

    /// <summary>
    /// Checks that the configuration names this media generation and that its configured set hashes equal the manifest packages.
    /// </summary>
    /// <exception cref="InvalidDataException">The configuration is not bound to <paramref name="package"/>.</exception>
    public static void ValidateConfigurationBinding(WinPeContentPackageMediaLease package, string deployConfigurationJson)
    {
        ThrowIfInvalidLease(package);
        var configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions);
        DeployCustomDriversSettings? settings = configuration?.CustomDrivers;
        if (settings is null || settings.ManifestId != package.ManifestId || !package.ManifestHash.Equals(settings.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CustomDrivers.InvalidMediaBinding");
        string[] configured = (settings.IsEnabled ? settings.Sets : [])
            .Select(set => set.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] supplied = package.Manifest.Packages.Select(entry => entry.ContentHash).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!configured.SequenceEqual(supplied, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("CustomDrivers.InvalidMediaBinding");
    }

    private static void ThrowIfInvalidLease(WinPeContentPackageMediaLease package)
    {
        ArgumentNullException.ThrowIfNull(package);
        package.ThrowIfDisposed();
        if (package.Kind != ContentPackageKind.Drivers)
            throw new ArgumentException($"The lease holds '{package.Kind.Name}' packages but driver sets need '{ContentPackageKind.Drivers.Name}' packages.", nameof(package));
    }
}
