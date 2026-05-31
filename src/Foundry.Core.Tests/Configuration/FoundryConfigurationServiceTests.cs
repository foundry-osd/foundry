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
            OperatingSystemSelection = new OperatingSystemSelectionSettings
            {
                AllowedLanguageCodes = ["en-US", "fr-FR"],
                DefaultLanguageCode = "fr-FR",
                AllowedReleaseIds = ["25H2"],
                DefaultReleaseId = "25H2",
                AllowedLicenseChannels = ["RET"],
                DefaultLicenseChannel = "RET",
                AllowedEditions = ["Pro"],
                DefaultEdition = "Pro"
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
                    PackageNames = ["Microsoft.BingWeather", "Microsoft.GamingApp"]
                },
                AiComponentRemoval = new AiComponentRemovalSettings
                {
                    IsEnabled = true,
                    RemoveCopilot = true,
                    RemoveAiHub = true,
                    DisableRecall = true,
                    DisableClickToDo = true,
                    DisableAiServiceAutoStart = true,
                    DisableEdgeAi = true,
                    DisablePaintAi = true,
                    DisableNotepadAi = true
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
        Assert.Equal(["en-US", "fr-FR"], loaded.OperatingSystemSelection.AllowedLanguageCodes);
        Assert.Equal("fr-FR", loaded.OperatingSystemSelection.DefaultLanguageCode);
        Assert.Equal(["25H2"], loaded.OperatingSystemSelection.AllowedReleaseIds);
        Assert.Equal("25H2", loaded.OperatingSystemSelection.DefaultReleaseId);
        Assert.Equal(["RET"], loaded.OperatingSystemSelection.AllowedLicenseChannels);
        Assert.Equal("RET", loaded.OperatingSystemSelection.DefaultLicenseChannel);
        Assert.Equal(["Pro"], loaded.OperatingSystemSelection.AllowedEditions);
        Assert.Equal("Pro", loaded.OperatingSystemSelection.DefaultEdition);
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
        Assert.Equal(["Microsoft.BingWeather", "Microsoft.GamingApp"], loaded.Customization.AppxRemoval.PackageNames);
        Assert.True(loaded.Customization.AiComponentRemoval.IsEnabled);
        Assert.True(loaded.Customization.AiComponentRemoval.RemoveCopilot);
        Assert.True(loaded.Customization.AiComponentRemoval.RemoveAiHub);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableRecall);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableClickToDo);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableAiServiceAutoStart);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableEdgeAi);
        Assert.True(loaded.Customization.AiComponentRemoval.DisablePaintAi);
        Assert.True(loaded.Customization.AiComponentRemoval.DisableNotepadAi);
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
    public void Deserialize_WhenAutopilotProvisioningModeIsMissing_DefaultsToJsonProfile()
    {
        var service = new FoundryConfigurationService();

        FoundryConfigurationDocument document = service.Deserialize("""
            {
              "schemaVersion": 7,
              "autopilot": {
                "isEnabled": true
              }
            }
            """);

        Assert.Equal(AutopilotProvisioningMode.JsonProfile, document.Autopilot.ProvisioningMode);
    }

    [Fact]
    public void Serialize_ThenDeserialize_WhenInteractiveHardwareHashModeIsSelected_PreservesReadableMode()
    {
        var service = new FoundryConfigurationService();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.InteractiveHardwareHashUpload
            }
        };

        string json = service.Serialize(document);
        FoundryConfigurationDocument loaded = service.Deserialize(json);

        Assert.Contains("\"provisioningMode\": \"interactiveHardwareHashUpload\"", json, StringComparison.Ordinal);
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, loaded.Autopilot.ProvisioningMode);
    }

    [Fact]
    public void Serialize_WhenHardwareHashSettingsArePersisted_DoesNotWritePrivateMaterial()
    {
        var service = new FoundryConfigurationService();
        var document = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
                HardwareHashUpload = new AutopilotHardwareHashUploadSettings
                {
                    Tenant = new AutopilotTenantRegistrationSettings
                    {
                        TenantId = "tenant-id",
                        ApplicationObjectId = "application-object-id",
                        ClientId = "client-id",
                        ServicePrincipalObjectId = "service-principal-object-id"
                    },
                    ActiveCertificate = new AutopilotCertificateMetadata
                    {
                        KeyId = "certificate-key-id",
                        Thumbprint = "ABCDEF123456",
                        DisplayName = "Foundry OSD Autopilot Registration",
                        ExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(6)
                    },
                    BootMediaCertificate = new AutopilotBootMediaCertificateSettings
                    {
                        PfxPath = @"E:\Secrets\foundry-osd-autopilot-registration.pfx",
                        PfxPassword = "PfxPassword-DoNotLeak",
                        ValidatedThumbprint = "ABCDEF123456",
                        ValidatedExpiresOnUtc = DateTimeOffset.UtcNow.AddMonths(6)
                    }
                }
            }
        };

        string json = service.Serialize(document);

        Assert.Contains("\"provisioningMode\": \"hardwareHashUpload\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("pfx", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PfxPassword-DoNotLeak", json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"E:\Secrets", json, StringComparison.OrdinalIgnoreCase);
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
