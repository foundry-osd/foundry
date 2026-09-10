// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Services.Runtime;
using Foundry.Core.Models.Runtime;
using Serilog;
using Serilog.Core;

namespace Foundry.Connect.Tests;

public sealed class RuntimeStartupDiagnosticsTests
{
    [Fact]
    public void FailureCaptureErrorStillPublishesFailedStatusWithoutRecordId()
    {
        string? stage = null;
        string? recordId = "unset";
        var startup = new RuntimeStartupDiagnostics(
            (value, _, id) => { stage = value; recordId = id; },
            (_, _) => throw new IOException("unavailable storage"), Logger.None);

        startup.ReportFailure(new InvalidOperationException(), "configuration");

        Assert.Equal(StartupStage.StartupFailed, stage);
        Assert.Null(recordId);
    }

    [Fact]
    public void FailureRecordIsPersistedBeforeStatusAndOriginalExceptionIsRetained()
    {
        var calls = new List<string>();
        var exception = new InvalidOperationException("original failure");
        Guid recordId = Guid.NewGuid();
        Exception? captured = null;
        var startup = new RuntimeStartupDiagnostics(
            (stage, category, id) => calls.Add(stage + ":" + id),
            (error, category) => { captured = error; calls.Add("persist"); return recordId; },
            Logger.None);

        startup.ReportFailure(exception, "configuration");
        startup.ReportFailure(exception, "configuration");
        startup.ReportUiReady();

        Assert.Same(exception, captured);
        Assert.Equal(["persist", StartupStage.StartupFailed + ":" + recordId.ToString("N")], calls);
    }

    [Fact]
    public void FailureAfterUsableUiRemainsOwnedByChild()
    {
        var stages = new List<string>();
        int captures = 0;
        var startup = new RuntimeStartupDiagnostics(
            (stage, category, id) => stages.Add(stage),
            (_, _) => { captures++; return Guid.NewGuid(); },
            Logger.None);

        startup.ReportUiReady();
        startup.ReportFailure(new InvalidOperationException(), "runtime");

        Assert.Equal(0, captures);
        Assert.Equal([StartupStage.UiReady, StartupStage.StartupFailed], stages);
    }

    [Fact]
    public async Task ReadinessWaitsForInitializationAndRenderedUi()
    {
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stages = new List<string>();
        var startup = new RuntimeStartupDiagnostics(
            (stage, _, _) => stages.Add(stage), (_, _) => null, Logger.None);

        Task observation = startup.ObserveInitializationAsync(() => initialized.Task, () => rendered.Task, () => true);
        Assert.Empty(stages);
        initialized.SetResult();
        Assert.Empty(stages);
        rendered.SetResult();
        await observation;

        Assert.Equal([StartupStage.UiReady], stages);
    }

    [Fact]
    public async Task ClosingDuringInitializationDoesNotReportReadiness()
    {
        var stages = new List<string>();
        var startup = new RuntimeStartupDiagnostics(
            (stage, _, _) => stages.Add(stage), (_, _) => null, Logger.None);

        await startup.ObserveInitializationAsync(() => Task.CompletedTask, () => Task.CompletedTask, () => false);

        Assert.Empty(stages);
    }
}
