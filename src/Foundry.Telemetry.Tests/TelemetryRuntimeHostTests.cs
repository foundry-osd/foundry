// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetryRuntimeHostTests
{
    [Theory]
    [InlineData(TelemetryEvents.BootstrapFailed)]
    [InlineData(TelemetryEvents.ConnectSessionReady)]
    [InlineData(TelemetryEvents.DeploySessionFinished)]
    public void Sanitize_ForWinPeEvents_RetainsOnlyTheFoundryWinPeHost(string eventName)
    {
        Assert.Equal(TelemetryRuntimeHosts.FoundryWinPe,
            TelemetryEventPropertyPolicy.Sanitize(eventName, Properties(TelemetryRuntimeHosts.FoundryWinPe))["runtime_host"]);
        Assert.False(TelemetryEventPropertyPolicy.Sanitize(eventName, Properties("foundry_connect")).ContainsKey("runtime_host"));
        Assert.False(TelemetryEventPropertyPolicy.Sanitize(eventName, Properties(1)).ContainsKey("runtime_host"));
    }

    [Theory]
    [InlineData(TelemetryEvents.OsdBootMediaFinished)]
    [InlineData(TelemetryEvents.AppDailyActive)]
    public void Sanitize_ForOtherEvents_DropsTheRuntimeHost(string eventName)
    {
        Assert.False(TelemetryEventPropertyPolicy.Sanitize(eventName, Properties(TelemetryRuntimeHosts.FoundryWinPe))
            .ContainsKey("runtime_host"));
    }

    [Theory]
    [InlineData(TelemetryEvents.BootstrapFailed)]
    [InlineData(TelemetryEvents.ConnectSessionReady)]
    [InlineData(TelemetryEvents.DeploySessionFinished)]
    public void BuildProperties_AddsTheRuntimeHostOfTheContext(string eventName)
    {
        Dictionary<string, object> properties = PostHogTelemetryService.BuildProperties(
            Context(TelemetryRuntimeHosts.FoundryWinPe), eventName, new Dictionary<string, object?>());

        Assert.Equal(TelemetryRuntimeHosts.FoundryWinPe, properties["runtime_host"]);
    }

    [Fact]
    public void BuildProperties_WithoutRuntimeHost_AddsNone()
    {
        Dictionary<string, object> properties = PostHogTelemetryService.BuildProperties(
            Context(runtimeHost: null), TelemetryEvents.ConnectSessionReady, new Dictionary<string, object?>());

        Assert.False(properties.ContainsKey("runtime_host"));
    }

    [Fact]
    public void BuildProperties_ForOtherEvents_DropsTheRuntimeHostOfTheContext()
    {
        Dictionary<string, object> properties = PostHogTelemetryService.BuildProperties(
            Context(TelemetryRuntimeHosts.FoundryWinPe), TelemetryEvents.OsdBootMediaFinished, new Dictionary<string, object?>());

        Assert.False(properties.ContainsKey("runtime_host"));
    }

    private static Dictionary<string, object?> Properties(object value) => new() { ["runtime_host"] = value };

    private static TelemetryContext Context(string? runtimeHost) => TelemetryContextFactory.Create(
        TelemetryApps.FoundryConnect, "1.2.3", "debug", TelemetryRuntimeModes.WinPe, TelemetryRuntimePayloadSources.Release,
        TelemetryBootMediaTargets.Usb, "x64", "en-US", runtimeHost);
}
