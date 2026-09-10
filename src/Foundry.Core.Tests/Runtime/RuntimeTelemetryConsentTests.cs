// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Xunit;

namespace Foundry.Core.Tests.Runtime;

public sealed class RuntimeTelemetryConsentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"telemetry\":{}}")]
    [InlineData("{\"schemaVersion\":2,\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true}}")]
    public void MissingOrInvalidBootstrapPreferencesNeverAuthorizeSending(string? json)
    {
        Assert.Null(RuntimeTelemetryConsent.ReadBootstrap(json));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PreferencesRemainIndependentBeforeSecretsAreDecrypted(bool usage, bool diagnostics)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            telemetry = new { isEnabled = usage, isRemoteDiagnosticsEnabled = diagnostics },
            protection = new { unreadableSecret = "not-decrypted" }
        });
        var settings = RuntimeTelemetryConsent.ReadBootstrap(json);
        Assert.NotNull(settings);
        Assert.Equal(usage, settings.IsEnabled);
        Assert.Equal(diagnostics, settings.IsRemoteDiagnosticsEnabled);
        Assert.Equal((usage, diagnostics), RuntimeTelemetryConsent.RestrictChild(json, true, true, true));
        Assert.Equal((false, false), RuntimeTelemetryConsent.RestrictChild(json, true, false, false));
    }

    [Fact]
    public void UnknownOverrideAndMalformedPreferencesRestrictDelivery()
    {
        Assert.Equal((false, false), RuntimeTelemetryConsent.RestrictChild(null, true, true, true));
        Assert.Equal((false, false), RuntimeTelemetryConsent.RestrictChild("invalid", false, true, true));
        Assert.Equal((true, false), RuntimeTelemetryConsent.RestrictChild(null, false, true, false));
        Assert.Equal((false, true), RuntimeTelemetryConsent.RestrictChild("{\"Telemetry\":{\"IsEnabled\":false}}", true, true, true));
    }

    [Theory]
    [InlineData("{\"telemetry\":{\"isEnabled\":true,\"IsEnabled\":false}}")]
    [InlineData("{\"telemetry\":{\"isRemoteDiagnosticsEnabled\":true,\"isRemoteDiagnosticsEnabled\":false}}")]
    [InlineData("{\"telemetry\":{\"isEnabled\":true},\"Telemetry\":{\"isEnabled\":false}}")]
    public void AmbiguousConsentFailsClosed(string json)
    {
        Assert.Equal((false, false), RuntimeTelemetryConsent.RestrictChild(json, true, true, true));
    }
}
