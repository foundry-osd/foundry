using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Configuration;

public sealed class ExpertConfigurationServiceTests
{
    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsBusinessSettings()
    {
        var service = new ExpertConfigurationService();

        var document = new FoundryExpertConfigurationDocument
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
        FoundryExpertConfigurationDocument loaded = service.Deserialize(json);

        Assert.True(loaded.Network.WifiProvisioned);
        Assert.Equal("CorpWiFi", loaded.Network.Wifi.Ssid);
        Assert.Equal("FD-", loaded.Customization.MachineNaming.Prefix);
        Assert.True(loaded.Customization.MachineNaming.AutoGenerateName);
        Assert.False(loaded.Customization.MachineNaming.AllowManualSuffixEdit);
        Assert.False(loaded.Telemetry.IsEnabled);
        Assert.Equal("install-id", loaded.Telemetry.InstallId);
        Assert.Equal("project-token", loaded.Telemetry.ProjectToken);
    }

    [Fact]
    public void Deserialize_WhenJsonIsNullLiteral_ReturnsDefaultDocument()
    {
        var service = new ExpertConfigurationService();

        FoundryExpertConfigurationDocument document = service.Deserialize("null");

        Assert.Equal(FoundryExpertConfigurationDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.False(document.Network.WifiProvisioned);
        Assert.False(document.Autopilot.IsEnabled);
    }

    [Fact]
    public void Deserialize_WhenAutopilotProvisioningModeIsMissing_DefaultsToJsonProfile()
    {
        var service = new ExpertConfigurationService();

        FoundryExpertConfigurationDocument document = service.Deserialize("""
            {
              "schemaVersion": 4,
              "autopilot": {
                "isEnabled": true
              }
            }
            """);

        Assert.Equal(AutopilotProvisioningMode.JsonProfile, document.Autopilot.ProvisioningMode);
    }

    [Fact]
    public void Serialize_WhenHardwareHashSettingsArePersisted_DoesNotWritePrivateMaterial()
    {
        var service = new ExpertConfigurationService();
        var document = new FoundryExpertConfigurationDocument
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
    }
}
