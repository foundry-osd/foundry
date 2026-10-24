// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// Pure functions that make a configuration portable between PCs: strip the values that belong to one PC, put the local
/// values back on an incoming configuration, and list the library content the configuration needs.
/// </summary>
/// <remarks>
/// The per-PC fields are one declarative table: a section that gains a per-PC value adds a row, not a code path.
/// Packages, driver sets and images are listed only when the media built from the configuration uses them, following the
/// readiness rules: an enabled section, an enabled action, an included set or image. Authoring files are always listed because
/// the configuration keeps their references.
/// </remarks>
public static class PortableConfiguration
{
    private static readonly FoundryConfigurationDocument Defaults = new();

    /// <summary>The values never exported or published, each copied from a source configuration onto a target.</summary>
    private static readonly PerPcField[] PerPcFields =
    [
        new("General.IsoOutputPath", (target, source) => target with { General = target.General with { IsoOutputPath = source.General.IsoOutputPath } }),
        new("General.CustomDriverDirectoryPath", (target, source) =>
            target with { General = target.General with { CustomDriverDirectoryPath = source.General.CustomDriverDirectoryPath } }),
        new("Telemetry", (target, source) => target with { Telemetry = source.Telemetry }),
    ];

    /// <summary>Returns <paramref name="configuration"/> with every per-PC value reset to its default.</summary>
    public static FoundryConfigurationDocument Strip(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Copy(configuration, Defaults);
    }

    /// <summary>Returns <paramref name="incoming"/> with the per-PC values of <paramref name="local"/>.</summary>
    public static FoundryConfigurationDocument ApplyLocal(FoundryConfigurationDocument incoming, FoundryConfigurationDocument local)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(local);
        return Copy(incoming, local);
    }

    /// <summary>
    /// Lists the library content the configuration needs, once per kind and hash: every authoring file reference, the packages
    /// of enabled post-installation actions, the included driver sets and the included custom images of enabled sections.
    /// </summary>
    public static IReadOnlyList<PackageContent> ListContents(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return FileReferences(configuration).Select(ToContent)
            .Concat(UsedPackages(configuration).Select(package => ToContent(package, SharedContentKind.Package)))
            .Concat(UsedDriverSets(configuration).Select(package => ToContent(package, SharedContentKind.DriverSet)))
            .Concat(UsedImages(configuration).Select(ToContent))
            .DistinctBy(content => (content.Kind, content.Hash.ToLowerInvariant()))
            .ToArray();
    }

    /// <summary>Gets every authoring file reference of the configuration, in the order of <see cref="MapFileReferences"/>.</summary>
    internal static IEnumerable<AuthoringFileReference> FileReferences(FoundryConfigurationDocument configuration)
    {
        var references = new List<AuthoringFileReference>();
        MapFileReferences(configuration, reference =>
        {
            references.Add(reference);
            return reference;
        });
        return references;
    }

    /// <summary>Returns the configuration with every authoring file reference replaced by <paramref name="map"/>.</summary>
    internal static FoundryConfigurationDocument MapFileReferences(
        FoundryConfigurationDocument configuration,
        Func<AuthoringFileReference, AuthoringFileReference> map)
    {
        NetworkSettings network = configuration.Network;
        AutopilotHardwareHashUploadSettings hardwareHash = configuration.Autopilot.HardwareHashUpload;
        return configuration with
        {
            Network = network with
            {
                Dot1x = network.Dot1x with { ProfileTemplate = Map(network.Dot1x.ProfileTemplate), Certificate = Map(network.Dot1x.Certificate) },
                Wifi = network.Wifi with { EnterpriseProfileTemplate = Map(network.Wifi.EnterpriseProfileTemplate), Certificate = Map(network.Wifi.Certificate) }
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
            reference is { ContentHash.Length: > 0 } ? map(reference) : reference;
    }

    /// <summary>Gets the packages of the enabled actions of an enabled post-installation section.</summary>
    internal static IEnumerable<ContentPackageReference> UsedPackages(FoundryConfigurationDocument configuration) =>
        configuration.PreOobe.IsEnabled
            ? configuration.PreOobe.Actions.Where(action => action.IsEnabled).Select(action => action.Package).OfType<ContentPackageReference>()
                .Where(package => package.ContentHash.Length > 0)
            : [];

    /// <summary>Gets the packages of the included driver sets of an enabled custom driver section.</summary>
    internal static IEnumerable<ContentPackageReference> UsedDriverSets(FoundryConfigurationDocument configuration) =>
        configuration.CustomDrivers.IsEnabled
            ? configuration.CustomDrivers.Sets.Where(set => set.IsIncluded).Select(set => set.Package).Where(package => package.ContentHash.Length > 0)
            : [];

    /// <summary>Gets the included images of an enabled custom image section.</summary>
    internal static IEnumerable<CustomImageReference> UsedImages(FoundryConfigurationDocument configuration) =>
        configuration.CustomImages.IsEnabled
            ? configuration.CustomImages.Images.Where(image => image.IsIncluded && image.ContentHash.Length > 0)
            : [];

    private static FoundryConfigurationDocument Copy(FoundryConfigurationDocument target, FoundryConfigurationDocument source) =>
        PerPcFields.Aggregate(target, (current, field) => field.Copy(current, source));

    /// <summary>Describes an authoring file as <see cref="SharedContentKind.File"/> content.</summary>
    internal static PackageContent ToContent(AuthoringFileReference reference) =>
        new(reference.ContentHash, SharedContentKind.File, reference.Length, reference.FileName);

    /// <summary>Describes a post-installation package or driver set package as content of <paramref name="kind"/>.</summary>
    internal static PackageContent ToContent(ContentPackageReference package, SharedContentKind kind) =>
        new(package.ContentHash, kind, package.Length, package.DisplayName);

    /// <summary>Describes a custom image as <see cref="SharedContentKind.Image"/> content.</summary>
    internal static PackageContent ToContent(CustomImageReference image) =>
        new(image.ContentHash, SharedContentKind.Image, image.Length, image.DisplayName);

    /// <summary>One per-PC value.</summary>
    /// <param name="Name">Dotted path of the value in the configuration.</param>
    /// <param name="Copy">Returns the target with the value taken from the source.</param>
    private sealed record PerPcField(string Name, Func<FoundryConfigurationDocument, FoundryConfigurationDocument, FoundryConfigurationDocument> Copy);
}
