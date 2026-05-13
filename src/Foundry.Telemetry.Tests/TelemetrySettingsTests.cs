using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetrySettingsTests
{
    [Fact]
    public void TelemetrySettings_DefaultsToEnabled()
    {
        var settings = new TelemetrySettings();

        Assert.True(settings.IsEnabled);
        Assert.Equal(TelemetryDefaults.PostHogEuHost, settings.HostUrl);
        Assert.Equal(TelemetryDefaults.ProjectToken, settings.ProjectToken);
    }

    [Fact]
    public void TelemetryOptions_WhenInstallIdIsMissing_DisablesTelemetry()
    {
        var options = new TelemetryOptions(
            IsEnabled: true,
            HostUrl: TelemetryDefaults.PostHogEuHost,
            ProjectToken: TelemetryDefaults.ProjectToken,
            InstallId: string.Empty);

        Assert.False(options.CanSend);
    }

    [Fact]
    public void TelemetryBuildConfiguration_CurrentIsLowCardinality()
    {
        Assert.Contains(TelemetryBuildConfiguration.Current, new[] { "debug", "release" });
    }

    [Fact]
    public void TelemetryRuntimeModes_UsesExpectedStableValues()
    {
        Assert.Equal("desktop", TelemetryRuntimeModes.Desktop);
        Assert.Equal("winpe", TelemetryRuntimeModes.WinPe);
        Assert.Equal("unknown", TelemetryRuntimeModes.Unknown);
    }

    [Fact]
    public void TelemetryRuntimePayloadSources_UsesExpectedStableValues()
    {
        Assert.Equal("none", TelemetryRuntimePayloadSources.None);
        Assert.Equal("debug", TelemetryRuntimePayloadSources.Debug);
        Assert.Equal("release", TelemetryRuntimePayloadSources.Release);
        Assert.Equal("unknown", TelemetryRuntimePayloadSources.Unknown);
    }
}
