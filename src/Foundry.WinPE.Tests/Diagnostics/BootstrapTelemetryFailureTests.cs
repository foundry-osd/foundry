// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;
using Foundry.WinPE.Diagnostics;
using Xunit;

namespace Foundry.WinPE.Tests.Diagnostics;

public sealed class BootstrapTelemetryFailureTests
{
    [Theory]
    [InlineData((int)BootstrapStage.Environment, "environment", "none")]
    [InlineData((int)BootstrapStage.Connect, "connect", TelemetryApps.FoundryConnect)]
    [InlineData((int)BootstrapStage.System, "system", TelemetryApps.FoundryConnect)]
    [InlineData((int)BootstrapStage.Deploy, "deploy", TelemetryApps.FoundryDeploy)]
    public void FailureIsAttributedToTheUiPhaseThatRan(int stage, string lastStage, string childApplication)
    {
        IReadOnlyDictionary<string, object?> properties = BootstrapTelemetry.FailureProperties(
            new BootstrapResult(BootstrapOutcome.Failed, (BootstrapStage)stage, 30, "child_exit"), TimeSpan.FromSeconds(12), "release",
            TelemetryBootMediaTargets.Usb);

        Assert.Equal(lastStage, properties["last_stage"]);
        Assert.Equal(childApplication, properties["child_application"]);
        Assert.Equal("child_exit", properties["failure_category"]);
        Assert.Equal(30, properties["child_exit_code"]);
    }

    [Fact]
    public void DeployCrashEventCarriesOnlyAllowedValuesIncludingTheRuntimeHost()
    {
        TelemetryContext context = BootstrapTelemetry.CreateContext("release", TelemetryBootMediaTargets.Usb);
        var properties = new Dictionary<string, object?>(BootstrapTelemetry.FailureProperties(
            new BootstrapResult(BootstrapOutcome.Failed, BootstrapStage.Deploy, 30, "child_exit"), TimeSpan.FromSeconds(12),
            "release", TelemetryBootMediaTargets.Usb))
        {
            [TelemetryEventPropertyPolicy.RuntimeHostProperty] = context.RuntimeHost
        };

        IReadOnlyDictionary<string, object?> sent = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.BootstrapFailed, properties);

        Assert.Equal(TelemetryApps.FoundryBootstrap, context.App);
        Assert.Equal(TelemetryRuntimeHosts.FoundryWinPe, sent[TelemetryEventPropertyPolicy.RuntimeHostProperty]);
        Assert.Equal(TelemetryApps.FoundryDeploy, sent["child_application"]);
        Assert.Equal("deploy", sent["last_stage"]);
        Assert.Equal("child_exit", sent["failure_category"]);
    }
}
