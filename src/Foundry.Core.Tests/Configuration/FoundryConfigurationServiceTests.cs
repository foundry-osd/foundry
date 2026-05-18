using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Configuration;

public sealed class FoundryConfigurationServiceTests
{
    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsBusinessSettings()
    {
        var service = new FoundryConfigurationService();

        var document = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                WifiProvisioned = true,
                Wifi = new WifiSettings
                {
                    IsEnabled = true,
                    Ssid = "CorpWiFi",
                    SecurityType = "WPA2/WPA3-Personal",
                    Passphrase = "supersecret"
                }
            },
            Customization = new CustomizationSettings
            {
                MachineNaming = new MachineNamingSettings
                {
                    IsEnabled = true,
                    Prefix = "FD-",
                    AutoGenerateName = true,
                    AllowManualSuffixEdit = false
                },
                Oobe = new OobeSettings
                {
                    IsEnabled = true,
                    SkipLicenseTerms = true,
                    DiagnosticDataLevel = OobeDiagnosticDataLevel.Off,
                    HidePrivacySetup = true,
                    AllowTailoredExperiences = false,
                    AllowAdvertisingId = false,
                    AllowOnlineSpeechRecognition = false,
                    AllowInkingAndTypingDiagnostics = false,
                    LocationAccess = OobeLocationAccessMode.ForceOff
                },
                AppxRemoval = new AppxRemovalSettings
                {
                    IsEnabled = true,
                    PackageNames = ["Microsoft.BingWeather", "Microsoft.Copilot"]
                }
            },
            Telemetry = new TelemetrySettings
            {
                IsEnabled = false,
                InstallId = "install-id",
                HostUrl = TelemetryDefaults.PostHogEuHost,
                ProjectToken = "project-token",
                RuntimePayloadSource = TelemetryRuntimePayloadSources.None
            }
        };

        string json = service.Serialize(document);
        FoundryConfigurationDocument loaded = service.Deserialize(json);

        Assert.True(loaded.Network.WifiProvisioned);
        Assert.Equal("CorpWiFi", loaded.Network.Wifi.Ssid);
        Assert.Equal("FD-", loaded.Customization.MachineNaming.Prefix);
        Assert.True(loaded.Customization.MachineNaming.AutoGenerateName);
        Assert.False(loaded.Customization.MachineNaming.AllowManualSuffixEdit);
        Assert.True(loaded.Customization.Oobe.IsEnabled);
        Assert.True(loaded.Customization.Oobe.SkipLicenseTerms);
        Assert.Equal(OobeDiagnosticDataLevel.Off, loaded.Customization.Oobe.DiagnosticDataLevel);
        Assert.True(loaded.Customization.Oobe.HidePrivacySetup);
        Assert.False(loaded.Customization.Oobe.AllowTailoredExperiences);
        Assert.False(loaded.Customization.Oobe.AllowAdvertisingId);
        Assert.False(loaded.Customization.Oobe.AllowOnlineSpeechRecognition);
        Assert.False(loaded.Customization.Oobe.AllowInkingAndTypingDiagnostics);
        Assert.Equal(OobeLocationAccessMode.ForceOff, loaded.Customization.Oobe.LocationAccess);
        Assert.True(loaded.Customization.AppxRemoval.IsEnabled);
        Assert.Equal(["Microsoft.BingWeather", "Microsoft.Copilot"], loaded.Customization.AppxRemoval.PackageNames);
        Assert.False(loaded.Telemetry.IsEnabled);
        Assert.Equal("install-id", loaded.Telemetry.InstallId);
        Assert.Equal("project-token", loaded.Telemetry.ProjectToken);
    }

    [Fact]
    public void Deserialize_WhenJsonIsNullLiteral_ReturnsDefaultDocument()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument document = service.Deserialize("null");

        Assert.Equal(FoundryConfigurationDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.False(document.Network.WifiProvisioned);
        Assert.False(document.Autopilot.IsEnabled);
    }

    [Fact]
    public void ApplyLegacyGeneralSettings_WhenAuthoringConfigHasNoGeneralSection_CopiesMediaDefaults()
    {
        var document = new FoundryConfigurationDocument();
        var legacyGeneral = new GeneralSettings
        {
            IsoOutputPath = @"E:\Foundry.iso",
            Architecture = Core.Services.WinPe.WinPeArchitecture.Arm64,
            WinPeLanguage = "fr-FR",
            UseCa2023 = true,
            UsbPartitionStyle = Core.Services.WinPe.UsbPartitionStyle.Mbr,
            UsbFormatMode = Core.Services.WinPe.UsbFormatMode.Complete,
            IncludeDellDrivers = true,
            IncludeHpDrivers = true,
            CustomDriverDirectoryPath = @"D:\Drivers"
        };

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplyLegacyGeneralSettings(
            document,
            legacyGeneral);

        Assert.Equal(legacyGeneral, migrated.General);
    }

    [Fact]
    public void ApplyLegacyGeneralSettings_WhenLegacyGeneralSettingsAreMissing_PreservesDocument()
    {
        var existingGeneral = new GeneralSettings
        {
            IsoOutputPath = @"C:\Existing.iso",
            Architecture = Core.Services.WinPe.WinPeArchitecture.X64,
            WinPeLanguage = "en-US",
            UseCa2023 = false
        };
        var document = new FoundryConfigurationDocument
        {
            General = existingGeneral
        };

        FoundryConfigurationDocument migrated = FoundryConfigurationMigration.ApplyLegacyGeneralSettings(
            document,
            legacyGeneralSettings: null);

        Assert.Equal(existingGeneral, migrated.General);
    }
}
