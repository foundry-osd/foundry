using System.Text.Json;
using Foundry.Deploy.Models.Configuration;
using Foundry.Telemetry;

namespace Foundry.Deploy.Tests;

public sealed class DeployConfigurationModelTests
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
    public void Deserialize_WhenOperatingSystemSelectionIsConfigured_PreservesPolicy()
    {
        const string json = """
            {
              "schemaVersion": 7,
              "operatingSystemSelection": {
                "allowedLanguageCodes": ["en-US", "fr-FR"],
                "defaultLanguageCode": "fr-FR",
                "allowedReleaseIds": ["25H2"],
                "defaultReleaseId": "25H2",
                "allowedLicenseChannels": ["RET"],
                "defaultLicenseChannel": "RET",
                "allowedEditions": ["Pro", "Enterprise"],
                "defaultEdition": "Pro"
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
        Assert.Equal(["en-US", "fr-FR"], document.OperatingSystemSelection.AllowedLanguageCodes);
        Assert.Equal("fr-FR", document.OperatingSystemSelection.DefaultLanguageCode);
        Assert.Equal(["25H2"], document.OperatingSystemSelection.AllowedReleaseIds);
        Assert.Equal("25H2", document.OperatingSystemSelection.DefaultReleaseId);
        Assert.Equal(["RET"], document.OperatingSystemSelection.AllowedLicenseChannels);
        Assert.Equal("RET", document.OperatingSystemSelection.DefaultLicenseChannel);
        Assert.Equal(["Pro", "Enterprise"], document.OperatingSystemSelection.AllowedEditions);
        Assert.Equal("Pro", document.OperatingSystemSelection.DefaultEdition);
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

        FoundryDeployConfigurationDocument? document = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(json, options);

        Assert.NotNull(document);
        Assert.Equal(AutopilotProvisioningMode.JsonProfile, document.Autopilot.ProvisioningMode);
    }

    [Fact]
    public void Deserialize_WhenInteractiveHardwareHashModeIsConfigured_PreservesProvisioningMode()
    {
        const string json = """
            {
              "schemaVersion": 2,
              "autopilot": {
                "isEnabled": true,
                "provisioningMode": "interactiveHardwareHashUpload"
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
        Assert.Equal(AutopilotProvisioningMode.InteractiveHardwareHashUpload, document.Autopilot.ProvisioningMode);
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
                  "defaultGroupTag": "Sales",
                  "knownGroupTags": ["Sales", "Kiosk"],
                  "certificatePfxSecret": {
                    "kind": "encrypted",
                    "algorithm": "aes-gcm-v1",
                    "keyId": "media",
                    "nonce": "nonce",
                    "tag": "tag",
                    "ciphertext": "ciphertext"
                  },
                  "certificatePfxPasswordSecret": {
                    "kind": "encrypted",
                    "algorithm": "aes-gcm-v1",
                    "keyId": "media",
                    "nonce": "password-nonce",
                    "tag": "password-tag",
                    "ciphertext": "password-ciphertext"
                  }
                }
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
        Assert.Equal(AutopilotProvisioningMode.HardwareHashUpload, document.Autopilot.ProvisioningMode);
        Assert.Equal("tenant-id", document.Autopilot.HardwareHashUpload.TenantId);
        Assert.Equal("client-id", document.Autopilot.HardwareHashUpload.ClientId);
        Assert.Equal("certificate-key-id", document.Autopilot.HardwareHashUpload.ActiveCertificateKeyId);
        Assert.Equal("ABCDEF123456", document.Autopilot.HardwareHashUpload.ActiveCertificateThumbprint);
        Assert.Equal(DateTimeOffset.Parse("2026-12-01T00:00:00+00:00"), document.Autopilot.HardwareHashUpload.ActiveCertificateExpiresOnUtc);
        Assert.Equal("Sales", document.Autopilot.HardwareHashUpload.DefaultGroupTag);
        Assert.Equal(["Sales", "Kiosk"], document.Autopilot.HardwareHashUpload.KnownGroupTags);
        Assert.Equal("encrypted", document.Autopilot.HardwareHashUpload.CertificatePfxSecret?.Kind);
        Assert.Equal("ciphertext", document.Autopilot.HardwareHashUpload.CertificatePfxSecret?.Ciphertext);
        Assert.Equal("encrypted", document.Autopilot.HardwareHashUpload.CertificatePfxPasswordSecret?.Kind);
        Assert.Equal("password-ciphertext", document.Autopilot.HardwareHashUpload.CertificatePfxPasswordSecret?.Ciphertext);
    }
}
