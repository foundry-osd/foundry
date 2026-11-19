// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;

namespace Foundry.Telemetry.Tests;

public sealed class TelemetryConnectSkippedTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sanitize_ForSessionReady_RetainsTheBooleanConnectSkipped(bool value)
    {
        IReadOnlyDictionary<string, object?> sanitized = TelemetryEventPropertyPolicy.Sanitize(
            TelemetryEvents.ConnectSessionReady, new Dictionary<string, object?> { ["connect_skipped"] = value });

        Assert.Equal(value, sanitized["connect_skipped"]);
    }

    [Theory]
    [InlineData("true")]
    [InlineData(1)]
    public void Sanitize_ForSessionReady_DropsANonBooleanConnectSkipped(object value)
    {
        Assert.False(TelemetryEventPropertyPolicy.Sanitize(
            TelemetryEvents.ConnectSessionReady, new Dictionary<string, object?> { ["connect_skipped"] = value }).ContainsKey("connect_skipped"));
    }

    [Fact]
    public void Sanitize_ForOtherEvents_DropsConnectSkipped()
    {
        Assert.False(TelemetryEventPropertyPolicy.Sanitize(
            TelemetryEvents.DeploySessionFinished, new Dictionary<string, object?> { ["connect_skipped"] = true }).ContainsKey("connect_skipped"));
    }
}
