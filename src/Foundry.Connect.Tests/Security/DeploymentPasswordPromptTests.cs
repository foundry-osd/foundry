// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;

namespace Foundry.Connect.Tests.Security;

public sealed class DeploymentPasswordPromptTests
{
    [Fact]
    public void OnDialogRendered_ReportsTheUiReady()
    {
        // Without the Bootstrap startup protocol variables the diagnostics only track the state.
        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.Create(
            new RuntimeApplicationIdentity("Foundry.Connect", TelemetryApps.FoundryConnect, "1.0.0.0"), "", configurationRequired: false);
        var prompt = new DeploymentPasswordPrompt(TestLocalization.Create(), startup);

        prompt.OnDialogRendered();

        Assert.True(startup.IsUiReady);
    }
}
