// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class FoundryConfigurationMigration
{
    /// <summary>Migrates only the known machine-local ISO default, preserving custom paths.</summary>
    public static FoundryConfigurationDocument MigrateDefaultIsoOutput(FoundryConfigurationDocument document, string oldDefaultPath, string newDefaultPath)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? output = document.General.IsoOutputPath;
        if (string.IsNullOrWhiteSpace(output)) return document;
        try
        {
            return string.Equals(Path.GetFullPath(output), Path.GetFullPath(oldDefaultPath), StringComparison.OrdinalIgnoreCase)
                ? document with { General = document.General with { IsoOutputPath = newDefaultPath } }
                : document;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return document; // Leave invalid custom values to normal configuration validation.
        }
    }

    private const int StructuredMachineNamingSchemaVersion = 14;
    private const int SupportedOperatingSystemReleasesSchemaVersion = 17;
    private const int LegacyRandomLength = 6;

    /// <summary>
    /// A file edited by hand may carry <c>null</c> where a list is expected. It means "none", so the lists become
    /// empty instead of failing later. A null domain entry is kept for validation to reject.
    /// </summary>
    private static DomainJoinSettings WithoutNullLists(DomainJoinSettings settings) => settings with
    {
        Domains = (settings.Domains ?? [])
            .Select(domain => domain is { OrganizationalUnits: null } ? domain with { OrganizationalUnits = [] } : domain).ToArray()
    };

    /// <summary>
    /// Replaces lists written as <c>null</c> with empty lists and keeps the first item of each id, so a hand-edited or
    /// imported document never reaches generation with ambiguous references.
    /// </summary>
    private static NetworkCacheSettings WithoutNullLists(NetworkCacheSettings settings) => settings with
    {
        Accounts = (settings.Accounts ?? []).DistinctBy(account => account.Id, StringComparer.Ordinal).ToArray(),
        Shares = (settings.Shares ?? []).DistinctBy(share => share.Id, StringComparer.Ordinal).ToArray(),
        Scopes = (settings.Scopes ?? [])
            .DistinctBy(scope => scope.Id, StringComparer.Ordinal)
            .Select(scope => scope with { ShareIds = (scope.ShareIds ?? []).Distinct(StringComparer.Ordinal).ToArray() })
            .ToArray()
    };

    public static FoundryConfigurationDocument ApplySchemaMigrations(FoundryConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        FoundryConfigurationDocument migrated = document.SchemaVersion < StructuredMachineNamingSchemaVersion
            ? MigrateMachineNaming(document)
            : document;

        migrated = migrated with { CustomImages = migrated.CustomImages ?? new CustomImagesSettings() };
        migrated = migrated with { CustomDrivers = migrated.CustomDrivers ?? new CustomDriverSetsSettings() };
        migrated = migrated with { PreOobe = migrated.PreOobe ?? new PreOobeSettings() };
        migrated = migrated with { NetworkCache = WithoutNullLists(migrated.NetworkCache ?? new NetworkCacheSettings()) };
        migrated = migrated with { DomainJoin = WithoutNullLists(migrated.DomainJoin ?? new DomainJoinSettings()) };
        DomainJoinConfigurationValidator.ThrowIfProvisioningModesConflict(migrated.Autopilot, migrated.DomainJoin);
        if (!DomainJoinConfigurationValidator.ValidateMetadata(migrated.DomainJoin).IsValid)
        {
            throw new InvalidOperationException("Domain join metadata is invalid.");
        }

        if (migrated.SchemaVersion < SupportedOperatingSystemReleasesSchemaVersion)
        {
            OperatingSystemSelectionSettings normalized = OperatingSystemSelectionSettingsNormalizer.Normalize(migrated.OperatingSystemSelection);
            migrated = migrated with
            {
                OperatingSystemSelection = migrated.OperatingSystemSelection with
                {
                    AllowedReleaseIds = normalized.AllowedReleaseIds,
                    DefaultReleaseId = normalized.DefaultReleaseId
                }
            };
        }

        return migrated.SchemaVersion < FoundryConfigurationDocument.CurrentSchemaVersion
            ? migrated with { SchemaVersion = FoundryConfigurationDocument.CurrentSchemaVersion }
            : migrated;
    }

    /// <summary>
    /// Reads and removes the legacy host-path members of an authoring document, whatever its schema number.
    /// </summary>
    /// <remarks>
    /// The conversion is shape-based: documents written by intermediate builds under the current schema number are handled
    /// too. A member that is not a string is removed and reads as absent. Every other member is left untouched.
    /// </remarks>
    /// <param name="root">The parsed document, modified in place.</param>
    /// <returns>The paths found; empty for a document already in the current shape.</returns>
    public static LegacyFilePaths ExtractLegacyFilePaths(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        JsonObject? network = root["network"] as JsonObject;
        JsonObject? dot1x = network?["dot1x"] as JsonObject;
        JsonObject? wifi = network?["wifi"] as JsonObject;
        JsonObject? bootMediaCertificate = root["autopilot"]?["hardwareHashUpload"]?["bootMediaCertificate"] as JsonObject;
        var unattendSourcePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root["unattend"]?["files"] is JsonArray files)
        {
            foreach (JsonObject file in files.OfType<JsonObject>())
            {
                string? sourcePath = Take(file, "sourcePath");
                if (sourcePath is not null && ReadString(file["id"]) is { Length: > 0 } id)
                {
                    unattendSourcePaths.TryAdd(id, sourcePath);
                }
            }
        }

        return new LegacyFilePaths(
            Take(dot1x, "profileTemplatePath"),
            Take(dot1x, "certificatePath"),
            Take(wifi, "enterpriseProfileTemplatePath"),
            Take(wifi, "certificatePath"),
            Take(bootMediaCertificate, "pfxPath"),
            unattendSourcePaths);

        static string? Take(JsonObject? owner, string name)
        {
            if (owner is null || !owner.Remove(name, out JsonNode? value)) return null;
            return ReadString(value) is { } path && !string.IsNullOrWhiteSpace(path) ? path : null;
        }

        static string? ReadString(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }

    /// <summary>
    /// Replaces legacy host paths with library references after typed deserialization.
    /// </summary>
    /// <remarks>
    /// <paramref name="importIfPresent"/> imports an existing file into the authoring file library and returns null when the
    /// file is missing or cannot be imported; the reference then stays null and the page asks for the file again. A reference
    /// already present is kept. An answer file is matched by id and accepted only when the imported bytes still match its
    /// validated <see cref="UnattendFileSettings.ContentHash"/>; otherwise the entry is kept without a file and reported unavailable.
    /// </remarks>
    public static FoundryConfigurationDocument ApplyLegacyFilePaths(
        FoundryConfigurationDocument document,
        LegacyFilePaths paths,
        Func<string, AuthoringFileReference?> importIfPresent)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(importIfPresent);

        NetworkSettings network = document.Network;
        AutopilotHardwareHashUploadSettings hardwareHash = document.Autopilot.HardwareHashUpload;
        return document with
        {
            Network = network with
            {
                Dot1x = network.Dot1x with
                {
                    ProfileTemplate = network.Dot1x.ProfileTemplate ?? Import(paths.Dot1xProfileTemplate),
                    Certificate = network.Dot1x.Certificate ?? Import(paths.Dot1xCertificate)
                },
                Wifi = network.Wifi with
                {
                    EnterpriseProfileTemplate = network.Wifi.EnterpriseProfileTemplate ?? Import(paths.WifiEnterpriseProfileTemplate),
                    Certificate = network.Wifi.Certificate ?? Import(paths.WifiCertificate)
                }
            },
            Autopilot = document.Autopilot with
            {
                HardwareHashUpload = hardwareHash with
                {
                    BootMediaCertificate = hardwareHash.BootMediaCertificate with
                    {
                        Pfx = hardwareHash.BootMediaCertificate.Pfx ?? Import(paths.AutopilotPfx)
                    }
                }
            },
            Unattend = document.Unattend with
            {
                Files = (document.Unattend.Files ?? []).Select(file => file is null || file.File is not null ||
                        !paths.UnattendSourcePaths.TryGetValue(file.Id, out string? sourcePath)
                    ? file!
                    : file with { File = Import(sourcePath) is { } imported && string.Equals(imported.ContentHash, file.ContentHash, StringComparison.OrdinalIgnoreCase) ? imported : null })
                    .ToArray()
            }
        };

        AuthoringFileReference? Import(string? path) => path is null ? null : importIfPresent(path);
    }

    public static FoundryConfigurationDocument ApplyLegacyGeneralSettings(
        FoundryConfigurationDocument document,
        GeneralSettings? legacyGeneralSettings)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (legacyGeneralSettings is null)
        {
            return document;
        }

        return document with { General = legacyGeneralSettings };
    }

    private static FoundryConfigurationDocument MigrateMachineNaming(FoundryConfigurationDocument document)
    {
        MachineNamingSettings legacy = document.Customization.MachineNaming;
        bool autoGenerateName = legacy.LegacyAutoGenerateName ?? false;
        MachineNamingSettings migrated = !legacy.IsEnabled
            ? new MachineNamingSettings()
            : autoGenerateName
                ? CreateGeneratedMachineNaming(legacy)
                : new MachineNamingSettings
                {
                    IsEnabled = true,
                    Mode = MachineNamingMode.Manual,
                    ManualInitialValue = legacy.LegacyPrefix,
                    AllowEditingDuringDeployment = true
                };

        return document with
        {
            Customization = document.Customization with { MachineNaming = migrated }
        };
    }

    private static MachineNamingSettings CreateGeneratedMachineNaming(MachineNamingSettings legacy)
    {
        var components = new List<MachineNameComponentSettings>();
        string? prefix = legacy.LegacyPrefix;
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            components.Add(new MachineNameComponentSettings
            {
                Type = MachineNameComponentType.StaticText,
                StaticText = prefix
            });
        }

        components.Add(new MachineNameComponentSettings
        {
            Type = MachineNameComponentType.Random,
            MaximumLength = LegacyRandomLength
        });

        return new MachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Composed,
            Components = components,
            Separator = MachineNameSeparator.None,
            Casing = MachineNameCasing.Preserve,
            AllowEditingDuringDeployment = legacy.LegacyAllowManualSuffixEdit ?? true
        };
    }
}
