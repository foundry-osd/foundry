// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class FoundryConfigurationMigration
{
    /// <summary>Migrates only the known machine-local ISO default, preserving custom and portable-profile paths.</summary>
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
    private const int LegacyRandomLength = 6;

    public static FoundryConfigurationDocument ApplySchemaMigrations(FoundryConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        FoundryConfigurationDocument migrated = document.SchemaVersion < StructuredMachineNamingSchemaVersion
            ? MigrateMachineNaming(document)
            : document;

        migrated = migrated with { CustomImages = migrated.CustomImages ?? new CustomImagesSettings() };

        return migrated.SchemaVersion < FoundryConfigurationDocument.CurrentSchemaVersion
            ? migrated with { SchemaVersion = FoundryConfigurationDocument.CurrentSchemaVersion }
            : migrated;
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
