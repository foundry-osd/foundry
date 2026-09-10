// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Core.Tests.TestUtilities;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Runtime;

public sealed class RuntimeTelemetryConsentFileTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Utf8BomPreservesConsentAndChildOptOut(bool bootstrapBom, bool childBom)
    {
        using var directory = new TemporaryDirectory();
        string bootstrap = WriteBootstrap(directory.Path);
        File.WriteAllText(bootstrap, File.ReadAllText(bootstrap), new System.Text.UTF8Encoding(bootstrapBom));
        string child = Path.Combine(directory.Path, "child.json");
        File.WriteAllText(child, "{\"telemetry\":{\"isEnabled\":false,\"isRemoteDiagnosticsEnabled\":true}}",
            new System.Text.UTF8Encoding(childBom));

        TelemetrySettings? settings = RuntimeTelemetryConsent.ReadSettings(bootstrap, child);

        Assert.NotNull(settings);
        Assert.False(settings.IsEnabled);
        Assert.True(settings.IsRemoteDiagnosticsEnabled);
    }

    [Theory]
    [InlineData("\"hostUrl\":\"https://other.example.test/Base?x=A\"")]
    [InlineData("\"hostUrl\":\"https://ingest.example.test/base?x=A\"")]
    [InlineData("\"hostUrl\":\"https://ingest.example.test/Base?x=a\"")]
    [InlineData("\"projectToken\":\"different-token\"")]
    [InlineData("\"installId\":\"different-installation\"")]
    [InlineData("\"installId\":null")]
    [InlineData("\"hostUrl\":42")]
    [InlineData("\"installId\":\"installation\",\"InstallId\":\"installation\"")]
    public void ExplicitDifferentOrAmbiguousChildDestinationDisablesEarlyTelemetry(string fields)
    {
        using var directory = new TemporaryDirectory();
        string bootstrap = WriteBootstrap(directory.Path);
        string child = Path.Combine(directory.Path, "child.json");
        File.WriteAllText(child, "{\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true," + fields + "}}");
        TelemetrySettings? settings = RuntimeTelemetryConsent.ReadSettings(bootstrap, child);
        Assert.NotNull(settings);
        Assert.False(settings.IsEnabled);
        Assert.False(settings.IsRemoteDiagnosticsEnabled);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CanonicalMatchingDestinationPreservesIndependentChildPreferences(bool usage, bool diagnostics)
    {
        using var directory = new TemporaryDirectory();
        string bootstrap = WriteBootstrap(directory.Path);
        string child = Path.Combine(directory.Path, "child.json");
        File.WriteAllText(child, System.Text.Json.JsonSerializer.Serialize(new
        {
            telemetry = new
            {
                isEnabled = usage,
                isRemoteDiagnosticsEnabled = diagnostics,
                hostUrl = "https://INGEST.EXAMPLE.TEST:443/Base/?x=A",
                projectToken = "public-token",
                installId = "installation"
            }
        }));
        TelemetrySettings? settings = RuntimeTelemetryConsent.ReadSettings(bootstrap, child);
        Assert.NotNull(settings);
        Assert.Equal(usage, settings.IsEnabled);
        Assert.Equal(diagnostics, settings.IsRemoteDiagnosticsEnabled);
    }

    private static string WriteBootstrap(string directory)
    {
        string path = Path.Combine(directory, "bootstrap.json");
        File.WriteAllText(path, """
            {"schemaVersion":1,"telemetry":{"isEnabled":true,"isRemoteDiagnosticsEnabled":true,
            "hostUrl":"https://ingest.example.test/Base?x=A","projectToken":"public-token","installId":"installation"}}
            """);
        return path;
    }

    [Fact]
    public void RequiredChildConfigurationMustBeResolvableBeforeEarlyTelemetry()
    {
        using var directory = new TemporaryDirectory();
        string bootstrap = Path.Combine(directory.Path, "bootstrap.json");
        File.WriteAllText(bootstrap, "{\"schemaVersion\":1,\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":true}}");
        string child = Path.Combine(directory.Path, "child.json");
        var missing = RuntimeTelemetryConsent.ReadSettings(bootstrap, child);
        Assert.NotNull(missing);
        Assert.False(missing.IsEnabled);
        Assert.False(missing.IsRemoteDiagnosticsEnabled);
        Assert.True(RuntimeTelemetryConsent.ReadSettings(bootstrap, child, childRequired: false)?.IsEnabled);
        Assert.False(RuntimeTelemetryConsent.ReadSettings(bootstrap, "relative.json", childRequired: false)?.IsEnabled);

        File.WriteAllText(child, "{\"telemetry\":{\"isEnabled\":true,\"isRemoteDiagnosticsEnabled\":false},\"encryptedSecret\":\"unreadable\"}");
        var restricted = RuntimeTelemetryConsent.ReadSettings(bootstrap, child);
        Assert.NotNull(restricted);
        Assert.True(restricted.IsEnabled);
        Assert.False(restricted.IsRemoteDiagnosticsEnabled);

        File.WriteAllText(child, new string(' ', 1024 * 1024 + 1));
        Assert.False(RuntimeTelemetryConsent.ReadSettings(bootstrap, child)?.IsEnabled);
        File.WriteAllText(bootstrap, "{}");
        Assert.Null(RuntimeTelemetryConsent.ReadSettings(bootstrap, child));
    }
}
