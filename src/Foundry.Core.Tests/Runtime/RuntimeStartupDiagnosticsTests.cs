// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Core.Models.Runtime;
using Foundry.Telemetry;
using Serilog.Core;

namespace Foundry.Core.Tests.Runtime;

[Collection(RuntimeStartupDiagnosticsCollection.Name)]
public sealed class RuntimeStartupDiagnosticsTests
{
    private static readonly RuntimeApplicationIdentity Identity =
        new("Foundry.Connect", TelemetryApps.FoundryConnect, "1.0.0.0");

    [Fact]
    public void HostedPhase_RoutesTheStagesToItsHooksAndNeverReportsTheProtocol()
    {
        var stages = new List<string>();
        var startup = RuntimeStartupDiagnostics.CreateForHostedPhase(
            Identity, () => stages.Add(StartupStage.UiReady), () => stages.Add(StartupStage.ConfigurationLoaded));

        startup.ReportConfigurationLoaded();
        startup.ReportUiReady();
        startup.ReportUiReady();

        Assert.Equal([StartupStage.ConfigurationLoaded, StartupStage.UiReady], stages);
        Assert.True(startup.IsUiReady);
    }

    [Fact]
    public void HostedPhase_FailureBeforeReadinessSuppressesTheHook()
    {
        int rendered = 0;
        var startup = RuntimeStartupDiagnostics.CreateForHostedPhase(Identity, () => rendered++);

        startup.ReportFailure(new InvalidOperationException(), "configuration");
        startup.ReportUiReady();

        Assert.Equal(0, rendered);
    }

    [Fact]
    public void FailureNormalizationErrorStillPublishesFailedStatus()
    {
        string? stage = null;
        var startup = new RuntimeStartupDiagnostics(Identity, (value, _) => stage = value, Logger.None);

        startup.ReportFailure(new UnrenderableException(), "configuration");

        Assert.Equal(StartupStage.StartupFailed, stage);
    }

    [Fact]
    public void FailureIsPublishedOnceAndSuppressesReadiness()
    {
        var calls = new List<string>();
        var startup = new RuntimeStartupDiagnostics(Identity, (stage, category) => calls.Add(stage + ":" + category), Logger.None);

        startup.ReportFailure(new InvalidOperationException("original failure"), "configuration");
        startup.ReportFailure(new InvalidOperationException("second failure"), "configuration");
        startup.ReportUiReady();

        Assert.Equal([StartupStage.StartupFailed + ":configuration"], calls);
        Assert.False(startup.IsUiReady);
    }

    private sealed class UnrenderableException : Exception
    {
        public override string ToString() => throw new InvalidOperationException("Exception formatting failed");
    }

    [Fact]
    public void FailureAfterUsableUiRemainsOwnedByChild()
    {
        var stages = new List<string>();
        var startup = new RuntimeStartupDiagnostics(Identity, (stage, _) => stages.Add(stage), Logger.None);

        startup.ReportUiReady();
        startup.ReportFailure(new InvalidOperationException(), "runtime");

        Assert.Equal([StartupStage.UiReady, StartupStage.StartupFailed], stages);
    }

    [Fact]
    public async Task ReadinessWaitsForInitializationAndRenderedUi()
    {
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stages = new List<string>();
        var startup = new RuntimeStartupDiagnostics(Identity, (stage, _) => stages.Add(stage), Logger.None);

        Task observation = startup.ObserveInitializationAsync(() => initialized.Task, () => rendered.Task, () => true);
        Assert.Empty(stages);
        initialized.SetResult();
        Assert.Empty(stages);
        Assert.False(startup.IsUiReady);
        rendered.SetResult();
        await observation;

        Assert.Equal([StartupStage.UiReady], stages);
        Assert.True(startup.IsUiReady);
    }

    [Fact]
    public async Task ClosingDuringInitializationDoesNotReportReadiness()
    {
        var stages = new List<string>();
        var startup = new RuntimeStartupDiagnostics(Identity, (stage, _) => stages.Add(stage), Logger.None);

        await startup.ObserveInitializationAsync(() => Task.CompletedTask, () => Task.CompletedTask, () => false);

        Assert.Empty(stages);
    }
}
