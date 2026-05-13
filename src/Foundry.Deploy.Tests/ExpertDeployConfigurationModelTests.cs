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
}
