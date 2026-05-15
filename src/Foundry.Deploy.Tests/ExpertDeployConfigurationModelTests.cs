using System.Text.Json;
using Foundry.Deploy.Models.Configuration;
using Foundry.Telemetry;

namespace Foundry.Deploy.Tests;

public sealed class ExpertDeployConfigurationModelTests
{
    [Fact]
    public void Deserialize_WhenLocalizationIncludesDefaultTimeZoneId_PreservesValue()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "localization": {
                "defaultTimeZoneId": "Romance Standard Time"
              }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, options);

        Assert.NotNull(document);
        Assert.Equal("Romance Standard Time", document.Localization.DefaultTimeZoneId);
    }

    [Fact]
    public void Deserialize_WhenTelemetryIsConfigured_PreservesTelemetrySettings()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "telemetry": {
                "isEnabled": false,
                "installId": "install-id",
                "hostUrl": "https://eu.i.posthog.com",
                "projectToken": "project-token",
                "runtimePayloadSource": "release"
              }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, options);

        Assert.NotNull(document);
        Assert.False(document.Telemetry.IsEnabled);
        Assert.Equal("install-id", document.Telemetry.InstallId);
        Assert.Equal(TelemetryDefaults.PostHogEuHost, document.Telemetry.HostUrl);
        Assert.Equal("project-token", document.Telemetry.ProjectToken);
        Assert.Equal(TelemetryRuntimePayloadSources.Release, document.Telemetry.RuntimePayloadSource);
    }

    [Fact]
    public void Deserialize_WhenAutopilotProvisioningModeIsMissing_DefaultsToJsonProfile()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "autopilot": {
                "isEnabled": true
              }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, options);

        Assert.NotNull(document);
        Assert.Equal(AutopilotProvisioningMode.JsonProfile, document.Autopilot.ProvisioningMode);
    }

    [Fact]
    public void Deserialize_WhenHardwareHashSettingsAreConfigured_PreservesRuntimeMetadata()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "autopilot": {
                "isEnabled": true,
                "provisioningMode": "hardwareHashUpload",
                "hardwareHashUpload": {
                  "tenantId": "tenant-id",
                  "clientId": "client-id",
                  "activeCertificateKeyId": "certificate-key-id",
                  "activeCertificateThumbprint": "ABCDEF123456",
                  "activeCertificateExpiresOnUtc": "2026-12-01T00:00:00+00:00",
                  "defaultGroupTag": "Sales"
                }
              }
            }
            """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, options);

        Assert.NotNull(document);
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, document.Autopilot.ProvisioningMode);
        Assert.Equal("tenant-id", document.Autopilot.HardwareHashUpload.TenantId);
        Assert.Equal("client-id", document.Autopilot.HardwareHashUpload.ClientId);
        Assert.Equal("certificate-key-id", document.Autopilot.HardwareHashUpload.ActiveCertificateKeyId);
        Assert.Equal("ABCDEF123456", document.Autopilot.HardwareHashUpload.ActiveCertificateThumbprint);
        Assert.Equal(DateTimeOffset.Parse("2026-12-01T00:00:00+00:00"), document.Autopilot.HardwareHashUpload.ActiveCertificateExpiresOnUtc);
        Assert.Equal("Sales", document.Autopilot.HardwareHashUpload.DefaultGroupTag);
    }
}
