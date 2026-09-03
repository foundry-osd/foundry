// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models.Configuration;
using DeployMachineNameComponentSettings = Foundry.Core.Models.Configuration.Deploy.DeployMachineNameComponentSettings;

namespace Foundry.Deploy.Services.Configuration;

internal static class DeployConfigurationMigration
{
    private const int StructuredMachineNamingSchemaVersion = 12;

    public static FoundryDeployConfigurationDocument ApplySchemaMigrations(FoundryDeployConfigurationDocument document)
    {
        if (document.SchemaVersion >= StructuredMachineNamingSchemaVersion)
        {
            return document;
        }

        DeployMachineNamingSettings legacy = document.Customization.MachineNaming;
        DeployMachineNamingSettings migrated = MigrateMachineNaming(legacy);
        return document with
        {
            Customization = document.Customization with { MachineNaming = migrated }
        };
    }

    private static DeployMachineNamingSettings MigrateMachineNaming(DeployMachineNamingSettings legacy)
    {
        if (!legacy.IsEnabled)
        {
            return new DeployMachineNamingSettings();
        }

        if (legacy.LegacyAutoGenerateName != true)
        {
            return new DeployMachineNamingSettings
            {
                IsEnabled = true,
                Mode = MachineNamingMode.Manual,
                ManualInitialValue = legacy.LegacyPrefix,
                AllowEditingDuringDeployment = true
            };
        }

        List<DeployMachineNameComponentSettings> components = [];
        if (!string.IsNullOrWhiteSpace(legacy.LegacyPrefix))
        {
            components.Add(new DeployMachineNameComponentSettings
            {
                Type = MachineNameComponentType.StaticText,
                StaticText = legacy.LegacyPrefix
            });
        }

        components.Add(new DeployMachineNameComponentSettings
        {
            Type = MachineNameComponentType.Random,
            MaximumLength = 6
        });

        return new DeployMachineNamingSettings
        {
            IsEnabled = true,
            Mode = MachineNamingMode.Composed,
            Components = components,
            AllowEditingDuringDeployment = legacy.LegacyAllowManualSuffixEdit ?? true
        };
    }
}
