// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.Console;
using Xunit;

namespace Foundry.WinPE.Tests.Console;

public sealed class BootstrapConsoleTests
{
    [Fact]
    public void CompletedStageRetainsWarningAndProvidesDiagnosticLocation()
    {
        using var output = new StringWriter();
        using var presenter = new BootstrapConsole(output);
        presenter.Report(new(BootstrapStage.Environment, BootstrapStatus.Running, "Preparing network access"));
        presenter.ReportWarning("Some network preparation was unavailable. Continuing.");
        presenter.Report(new(BootstrapStage.Environment, BootstrapStatus.Completed, "Environment prepared"));
        presenter.Report(new(BootstrapStage.Connect, BootstrapStatus.Completed, "Foundry closed."));
        presenter.Complete(new(BootstrapOutcome.Succeeded, BootstrapStage.Connect), "SESSION", @"X:\Foundry\Logs\FoundryBootstrap.log");

        string text = output.ToString();
        Assert.Contains("Done (warning)", text);
        Assert.Contains("Some network preparation was unavailable. Continuing.", text);
        Assert.Contains("Foundry closed.", text);
        Assert.Contains("Session: SESSION", text);
        Assert.Contains(@"X:\Foundry\Logs\FoundryBootstrap.log", text);
    }

    [Fact]
    public void LateCallbacksCannotReplaceTheOutcome()
    {
        using var output = new StringWriter();
        using var presenter = new BootstrapConsole(output);
        presenter.Report(new(BootstrapStage.Connect, BootstrapStatus.Completed, "Foundry closed."));
        presenter.Complete(new(BootstrapOutcome.Succeeded, BootstrapStage.Deploy), "SESSION", null);
        string terminal = output.ToString();

        presenter.Report(new(BootstrapStage.Deploy, BootstrapStatus.Failed, "Late failure"));
        presenter.ReportWarning("Late warning");
        presenter.Complete(new(BootstrapOutcome.Failed, BootstrapStage.Deploy), "SESSION", null);

        Assert.Equal(terminal, output.ToString());
        Assert.DoesNotContain("Foundry Deploy", terminal);
        Assert.DoesNotContain('\u001b', terminal);
    }

    [Fact]
    public void EveryUiPhaseIsShownOnTheSingleFoundryLine()
    {
        using var output = new StringWriter();
        using var presenter = new BootstrapConsole(output);
        presenter.Report(new(BootstrapStage.Connect, BootstrapStatus.Running, "Starting Foundry"));
        presenter.Report(new(BootstrapStage.Deploy, BootstrapStatus.Failed, "Foundry stopped unexpectedly."));

        string text = output.ToString();
        Assert.Contains("Foundry: In progress - Starting Foundry", text);
        Assert.Contains("Foundry: Failed - Foundry stopped unexpectedly.", text);
        Assert.DoesNotContain("Clock and time zone", text);
        Assert.DoesNotContain("Deployment application", text);
        Assert.DoesNotContain("Network connection", text);
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
