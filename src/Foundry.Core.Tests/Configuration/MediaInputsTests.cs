// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Media;

namespace Foundry.Core.Tests.Configuration;

public sealed class MediaInputsTests
{
    [Fact]
    public void ToString_NeverPrintsPasswords()
    {
        var network = new NetworkSecrets { WifiPassphrase = "Secret-Passphrase", WiredCertificatePfxPassword = "Secret-Wired", WifiCertificatePfxPassword = "Secret-Wifi" };

        string printed = string.Join(" ", network.ToString(),
            new MediaInputs { Network = network, AutopilotPfxPassword = "Secret-Autopilot" }.ToString(),
            new BuildSnapshotSources(new AuthoringFileLibrary(Path.GetTempPath())) { Network = network, AutopilotPfxPassword = "Secret-Autopilot" }.ToString());

        Assert.DoesNotContain("Secret-", printed, StringComparison.Ordinal);
        Assert.Contains("HasAutopilotPfxPassword = True", printed, StringComparison.Ordinal);
    }
}
