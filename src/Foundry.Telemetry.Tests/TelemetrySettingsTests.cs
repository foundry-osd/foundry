// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetrySettingsTests
{
    [Fact]
    public void TelemetrySettings_DefaultsToEnabled()
    {
        var settings = new TelemetrySettings();

        Assert.True(settings.IsEnabled);
        Assert.False(settings.IsRemoteDiagnosticsEnabled);
        Assert.Equal(TelemetryDefaults.PostHogEuHost, settings.HostUrl);
        Assert.Equal(TelemetryDefaults.ProjectToken, settings.ProjectToken);
    }

    [Theory]
    [InlineData(false, "https://eu.i.posthog.com", "token", "install", false)]
    [InlineData(true, "", "token", "install", false)]
    [InlineData(true, "not-a-uri", "token", "install", false)]
    [InlineData(true, "http://eu.i.posthog.com", "token", "install", false)]
    [InlineData(true, "https://eu.i.posthog.com", "", "install", false)]
    [InlineData(true, "https://eu.i.posthog.com", "token", "", false)]
    [InlineData(true, "https://eu.i.posthog.com", "token", "install", true)]
    public void RemoteDiagnosticsOptions_CanSendRequiresCompleteConfiguration(
        bool isEnabled,
        string hostUrl,
        string projectToken,
        string installId,
        bool expected)
    {
        var options = new RemoteDiagnosticsOptions(isEnabled, hostUrl, projectToken, installId);

        Assert.Equal(expected, options.CanSend);
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
