// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Core.Tests.TestUtilities;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Runtime;

public sealed class RuntimeTelemetryConsentFileTests
{
    private const string Configuration =
        "{\"schemaVersion\":1,\"telemetry\":{\"isEnabled\":false,\"isRemoteDiagnosticsEnabled\":true},\"deploy\":{}}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsTheConsentOfTheWinPeConfigurationWithOrWithoutBom(bool bom)
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "foundry.winpe.config.json");
        File.WriteAllText(path, Configuration, new System.Text.UTF8Encoding(bom));

        TelemetrySettings? settings = RuntimeTelemetryConsent.ReadSettings(path);

        Assert.NotNull(settings);
        Assert.False(settings.IsEnabled);
        Assert.True(settings.IsRemoteDiagnosticsEnabled);
    }

    [Fact]
    public void MissingRelativeOrOversizedConfigurationNeverAuthorizesSending()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "foundry.winpe.config.json");

        Assert.Null(RuntimeTelemetryConsent.ReadSettings(path));
        Assert.Null(RuntimeTelemetryConsent.ReadSettings("foundry.winpe.config.json"));
        File.WriteAllText(path, Configuration + new string(' ', 1024 * 1024));
        Assert.Null(RuntimeTelemetryConsent.ReadSettings(path));
    }
}
