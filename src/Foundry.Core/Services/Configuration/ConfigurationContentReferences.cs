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

    /// <summary>
    /// Gets the content hashes of every authoring file reference: the 802.1X and Wi-Fi profile templates, client certificates and
    /// trusted roots, the Autopilot boot media certificate and every answer file.
    /// </summary>
    public static IReadOnlySet<string> AuthoringFileHashes(FoundryConfigurationDocument configuration) =>
        ToSet(AuthoringFiles(configuration).Select(reference => reference.ContentHash));

    /// <summary>Gets every authoring file reference of the configuration, in the order of <see cref="MapAuthoringFiles"/>.</summary>
    public static IReadOnlyList<AuthoringFileReference> AuthoringFiles(FoundryConfigurationDocument configuration)
    {
        var references = new List<AuthoringFileReference>();
        MapAuthoringFiles(configuration, reference =>
        {
            references.Add(reference);
            return reference;
        });
        return references;
    }

    /// <summary>
    /// Returns the configuration with every authoring file reference replaced by <paramref name="map"/>. This is the single list of
    /// file-bearing settings: cleanup, export and import all walk it, so a new setting that holds a file is added here only.
    /// </summary>
    /// <remarks>References without a content hash are left unchanged and not passed to <paramref name="map"/>.</remarks>
    public static FoundryConfigurationDocument MapAuthoringFiles(
        FoundryConfigurationDocument configuration,
        Func<AuthoringFileReference, AuthoringFileReference> map)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(map);
        NetworkSettings network = configuration.Network;
        AutopilotHardwareHashUploadSettings hardwareHash = configuration.Autopilot.HardwareHashUpload;
        return configuration with
        {
            Network = network with
            {
                Dot1x = network.Dot1x with
                {
                    ProfileTemplate = Map(network.Dot1x.ProfileTemplate),
                    ClientCertificate = Map(network.Dot1x.ClientCertificate),
                    TrustedRootCertificates = MapAll(network.Dot1x.TrustedRootCertificates)
                },
                Wifi = network.Wifi with
                {
                    EnterpriseProfileTemplate = Map(network.Wifi.EnterpriseProfileTemplate),
                    ClientCertificate = Map(network.Wifi.ClientCertificate),
                    TrustedRootCertificates = MapAll(network.Wifi.TrustedRootCertificates)
                }
            },
            Autopilot = configuration.Autopilot with
            {
                HardwareHashUpload = hardwareHash with
                {
                    BootMediaCertificate = hardwareHash.BootMediaCertificate with { Pfx = Map(hardwareHash.BootMediaCertificate.Pfx) }
                }
            },
            Unattend = configuration.Unattend with
            {
                Files = configuration.Unattend.Files.Select(file => file with { File = Map(file.File) }).ToArray()
            }
        };

        AuthoringFileReference? Map(AuthoringFileReference? reference) =>
            string.IsNullOrWhiteSpace(reference?.ContentHash) ? reference : map(reference);

        AuthoringFileReference[] MapAll(IReadOnlyList<AuthoringFileReference> references) =>
            references.Select(reference => Map(reference)!).ToArray();
    }

    private static HashSet<string> ToSet(IEnumerable<string?> hashes) =>
        new(hashes.Where(hash => !string.IsNullOrWhiteSpace(hash))!, StringComparer.OrdinalIgnoreCase);
}
