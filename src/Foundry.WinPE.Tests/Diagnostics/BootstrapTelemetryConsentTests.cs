// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Diagnostics;
using Xunit;

namespace Foundry.WinPE.Tests.Diagnostics;

public sealed class BootstrapTelemetryConsentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryBootstrapTests", Guid.NewGuid().ToString("N"));

    public BootstrapTelemetryConsentTests() => Directory.CreateDirectory(Path.Combine(root, "Config"));

    [Fact]
    public void ReadConsent_ReadsTheWinPeConfigurationFromTheDefaultPath()
    {
        WriteConsent(Path.Combine(root, "Config", "foundry.winpe.config.json"), "default");

        Assert.Equal("default", BootstrapTelemetry.ReadConsent(root, configurationOverride: null)?.InstallId);
    }

    [Fact]
    public void ReadConsent_HonorsTheConfigurationOverride()
    {
        WriteConsent(Path.Combine(root, "Config", "foundry.winpe.config.json"), "default");
        string overridePath = Path.Combine(root, "override.json");
        WriteConsent(overridePath, "override");

        Assert.Equal("override", BootstrapTelemetry.ReadConsent(root, overridePath)?.InstallId);
        Assert.Null(BootstrapTelemetry.ReadConsent(root, Path.Combine(root, "missing.json")));
    }

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    private static void WriteConsent(string path, string installId) => File.WriteAllText(path, $$$"""
        {"schemaVersion":1,"telemetry":{"isEnabled":false,"isRemoteDiagnosticsEnabled":false,"installId":"{{{installId}}}"}}
        """);
}
