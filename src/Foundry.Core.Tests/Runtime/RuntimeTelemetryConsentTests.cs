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
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"telemetry\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"telemetry\":{\"isEnabled\":true}}")]
    [InlineData("{\"schemaVersion\":0,\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true}}")]
    [InlineData("{\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true}}")]
    [InlineData("{\"schemaVersion\":\"1\",\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true}}")]
    public void MissingOrInvalidPreferencesNeverAuthorizeSending(string? json)
    {
        Assert.Null(RuntimeTelemetryConsent.Read(json));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PreferencesRemainIndependentWithoutReadingSecrets(bool usage, bool diagnostics)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            telemetry = new { isEnabled = usage, isRemoteDiagnosticsEnabled = diagnostics, installId = "installation" },
            protection = new { unreadableSecret = "not-decrypted" },
            deploy = new { domainJoin = "not-a-section" }
        });

        var settings = RuntimeTelemetryConsent.Read(json);

        Assert.NotNull(settings);
        Assert.Equal(usage, settings.IsEnabled);
        Assert.Equal(diagnostics, settings.IsRemoteDiagnosticsEnabled);
        Assert.Equal("installation", settings.InstallId);
    }

    [Fact]
    public void TrailingCommasAndCommentsYieldTheSameConsentAsTheModeReaders()
    {
        const string strict = """{"schemaVersion":1,"telemetry":{"isEnabled":true,"isRemoteDiagnosticsEnabled":false,"installId":"installation"}}""";
        const string handWritten = """
            {
              // Hand-edited on the media.
              "schemaVersion": 1,
              "telemetry": { "isEnabled": true, "isRemoteDiagnosticsEnabled": false, "installId": "installation", },
            }
            """;

        Assert.Equal(RuntimeTelemetryConsent.Read(strict), RuntimeTelemetryConsent.Read(handWritten));
        Assert.NotNull(RuntimeTelemetryConsent.Read(handWritten));
    }

    [Fact]
    public void NewerSchemaKeepsItsPreferences()
    {
        var settings = RuntimeTelemetryConsent.Read(
            "{\"schemaVersion\":2,\"Telemetry\":{\"IsEnabled\":true,\"IsRemoteDiagnosticsEnabled\":false}}");

        Assert.NotNull(settings);
        Assert.True(settings.IsEnabled);
        Assert.False(settings.IsRemoteDiagnosticsEnabled);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"telemetry\":{\"isEnabled\":true,\"IsEnabled\":false,\"isRemoteDiagnosticsEnabled\":true}}")]
    [InlineData("{\"schemaVersion\":1,\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true},\"Telemetry\":{\"isEnabled\":false}}")]
    [InlineData("{\"schemaVersion\":1,\"SchemaVersion\":1,\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true}}")]
    public void AmbiguousConsentFailsClosed(string json)
    {
        Assert.Null(RuntimeTelemetryConsent.Read(json));
    }
}
