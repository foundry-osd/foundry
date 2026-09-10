// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Bootstrap.Console;
using Foundry.Bootstrap.Runtime;
using Xunit;

namespace Foundry.Bootstrap.Tests.Console;

public sealed class BootstrapConsoleTests
{
    [Fact]
    public void CompletedStageRetainsWarningAndProvidesDiagnosticLocation()
    {
        using var output = new StringWriter();
        using var presenter = new BootstrapConsole(output);
        presenter.Report(new(BootstrapStage.System, BootstrapStatus.Running, "Preparing the clock"));
        presenter.ReportWarning("Time zone could not be applied.");
        presenter.Report(new(BootstrapStage.System, BootstrapStatus.Completed, "System preparation completed"));
        presenter.Complete(new(BootstrapOutcome.Succeeded, BootstrapStage.Deploy, ReadinessConfirmed: true), "SESSION", @"X:\Foundry\Logs\FoundryBootstrap.log");

        string text = output.ToString();
        Assert.Contains("Done (warning)", text);
        Assert.Contains("Time zone could not be applied.", text);
        Assert.Contains("Continue in Foundry Deploy.", text);
        Assert.Contains("Session: SESSION", text);
        Assert.Contains(@"X:\Foundry\Logs\FoundryBootstrap.log", text);
    }

    [Fact]
    public void LegacyHandoffNeverClaimsReadinessAndLateCallbacksCannotReplaceOutcome()
    {
        using var output = new StringWriter();
        using var presenter = new BootstrapConsole(output);
        presenter.Complete(new(BootstrapOutcome.Succeeded, BootstrapStage.Deploy), "SESSION", null);
        string terminal = output.ToString();

        presenter.Report(new(BootstrapStage.Deploy, BootstrapStatus.Failed, "Late failure"));
        presenter.ReportWarning("Late warning");
        presenter.ReportDownload(new RuntimeDownloadProgress("Foundry.Deploy", 100, 100));
        presenter.Complete(new(BootstrapOutcome.Failed, BootstrapStage.Deploy), "SESSION", null);

        Assert.Equal(terminal, output.ToString());
        Assert.Contains("Readiness is unverified", terminal);
        Assert.DoesNotContain("Continue in Foundry Deploy", terminal);
        Assert.DoesNotContain('\u001b', terminal);
    }

    [Fact]
    public void NormalCancellationDoesNotDisplayDiagnosticFooter()
    {
        using var output = new StringWriter();
        using var presenter = new BootstrapConsole(output);
        presenter.Report(new(BootstrapStage.Connect, BootstrapStatus.Cancelled, "Boot was cancelled. Deployment will not continue."));
        presenter.Complete(new(BootstrapOutcome.Cancelled, BootstrapStage.Connect, 20), "SESSION", @"X:\Foundry\Logs\FoundryBootstrap.log");

        Assert.Contains("Startup cancelled", output.ToString());
        Assert.DoesNotContain("Session:", output.ToString());
        Assert.DoesNotContain("Log:", output.ToString());
    }

    [Fact]
    public void UnavailableOutputDoesNotInterruptBootReporting()
    {
        using var output = new UnavailableWriter();
        using var presenter = new BootstrapConsole(output);
        presenter.Report(new(BootstrapStage.Connect, BootstrapStatus.Running, "Waiting for Connect"));
        presenter.ReportWarning("Network preparation was unavailable.");
        presenter.Report(new(BootstrapStage.Connect, BootstrapStatus.Failed, "Connect failed"));
        presenter.Complete(new(BootstrapOutcome.Failed, BootstrapStage.Connect), "SESSION", null);
    }

    private sealed class UnavailableWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("Console closed");
    }
}
