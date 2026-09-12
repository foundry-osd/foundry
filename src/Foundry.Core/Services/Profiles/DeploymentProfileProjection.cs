// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;

namespace Foundry.Core.Services.Profiles;

/// <summary>Removes host paths, session secrets, runtime envelopes and local telemetry before transfer or activation.</summary>
public static class DeploymentProfileProjection
{
    public static FoundryConfigurationDocument CreatePortable(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion < 1 || configuration.SchemaVersion > FoundryConfigurationDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException("The profile authoring schema is unsupported.");
        }

        if (configuration.General is null || configuration.Network?.Wifi is null || configuration.Network.Dot1x is null
            || configuration.Customization?.Oobe?.AdditionalAccounts is null || configuration.Unattend?.Files is null
            || configuration.Autopilot?.HardwareHashUpload is null || configuration.Autopilot.Profiles is null
            || configuration.OperatingSystemSelection is null || configuration.Localization is null || configuration.General.DeploymentProtection is null
            || configuration.Customization.MachineNaming is null || configuration.Customization.AppxRemoval is null
            || configuration.Customization.WindowsOptionalFeatures is null || configuration.Customization.AiComponentRemoval is null
            || configuration.Autopilot.HardwareHashUpload.Tenant is null)
        {
            throw new InvalidDataException("The profile configuration is incomplete.");
        }

        FoundryConfigurationDocument portable = configuration with
        {
            General = configuration.General with { IsoOutputPath = null, CustomDriverDirectoryPath = null },
            Network = configuration.Network with
            {
                Wifi = configuration.Network.Wifi with
                {
                    Passphrase = null,
                    PassphraseSecret = null,
                    CertificatePfxPassword = null,
                    CertificatePfxPasswordSecret = null,
                    CertificatePath = null,
                    EnterpriseProfileTemplatePath = null
                },
                Dot1x = configuration.Network.Dot1x with
                {
                    CertificatePfxPassword = null,
                    CertificatePfxPasswordSecret = null,
                    CertificatePath = null,
                    ProfileTemplatePath = null
                }
            },
            Unattend = configuration.Unattend with
            {
                Files = configuration.Unattend.Files.Select(file => file is null
                    ? throw new InvalidDataException("The profile contains an invalid answer file.")
                    : file with { SourcePath = string.Empty }).ToArray()
            },
            Autopilot = configuration.Autopilot with
            {
                HardwareHashUpload = configuration.Autopilot.HardwareHashUpload with { BootMediaCertificate = new() }
            },
            Telemetry = new TelemetrySettings { IsEnabled = false, IsRemoteDiagnosticsEnabled = false, InstallId = string.Empty, HostUrl = string.Empty, ProjectToken = string.Empty }
        };
        return FoundryConfigurationMigration.ApplySchemaMigrations(portable);
    }
}
