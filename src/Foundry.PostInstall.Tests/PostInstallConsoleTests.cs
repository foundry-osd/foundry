// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Console;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class PostInstallConsoleTests
{
    [Fact]
    public void DomainOutcomes_ShowIndependentPhasesAndPlacementWarning()
    {
        using var f = new DomainFixture();
        var report = new DomainJoinResultStore(f.Root, f.Plan, f.Hash); report.Seed();
        report.Write(report.Read() with
        {
            OriginatingBootId = "installed-boot",
            Join = new() { State = DomainJoinPhaseState.Succeeded },
            Placement = new() { State = DomainJoinPhaseState.Failed, FailureCode = DomainJoinFailureCode.PlacementFailed },
            Restart = DomainJoinRestartState.Required
        });
        using var output = new StringWriter();
        using var console = new PostInstallConsole(f.Plan, "test.log", output);
        console.Report(new([], "CompletedWithErrors", DomainResult: report.Read(), WarningCount: 1));
        console.Complete(new("CompletedWithErrors", 0));
        string text = output.ToString();
        Assert.Contains("Domain joined; target OU placement failed", text);
        Assert.Contains("Join: Succeeded", text);
        Assert.Contains("Placement: Failed", text);
        Assert.Contains("Membership: NotStarted", text);
        Assert.Contains("Restart: Required", text);
        Assert.Contains("Cleanup: Pending", text);
        Assert.Contains("Warnings: 1", text);
    }

    [Theory]
    [InlineData(DomainJoinPhaseState.Succeeded, DomainJoinPhaseState.Unverified, "Domain joined; target OU placement not confirmed")]
    [InlineData(DomainJoinPhaseState.Succeeded, DomainJoinPhaseState.Unknown, "Domain joined; target OU placement not confirmed")]
    [InlineData(DomainJoinPhaseState.Unknown, DomainJoinPhaseState.Skipped, "Domain join outcome unknown; membership is checked after restart")]
    [InlineData(DomainJoinPhaseState.Succeeded, DomainJoinPhaseState.Failed, "Domain joined in the default location; target OU not found")]
    public void DomainOutcomes_ExplainUnconfirmedStates(DomainJoinPhaseState join, DomainJoinPhaseState placement, string expected)
    {
        using var f = new DomainFixture();
        var report = new DomainJoinResultStore(f.Root, f.Plan, f.Hash); report.Seed();
        DomainJoinResult result = report.Read() with
        {
            Join = new() { State = join },
            Placement = new() { State = placement, FailureCode = placement == DomainJoinPhaseState.Failed ? DomainJoinFailureCode.OrganizationalUnitNotFound : null }
        };
        using var output = new StringWriter();
        using var console = new PostInstallConsole(f.Plan, "test.log", output);
        console.Report(new([], "Running", DomainResult: result, WarningCount: 1));
        Assert.Contains(expected, output.ToString());
    }

    [Fact]
    public void RedirectedOutput_ReportsResumedResultsAndOnlyChangedActions()
    {
        using var output = new StringWriter();
        using var console = new PostInstallConsole(new(), "test.log", output);
        var resumed = new PostInstallProgress([
            new("first", "Configure Windows", "Succeeded", TimeSpan.FromSeconds(3), 0),
            new("second", "Install software", "Running")], "Running", IsResuming: true);
        console.Report(resumed);
        console.Report(resumed);
        console.Report(resumed with { Actions = [resumed.Actions[0], resumed.Actions[1] with { Status = "Failed", ExitCode = 42 }] });
        console.Complete(new("CompletedWithErrors", 0));
        string text = output.ToString();
        Assert.Contains("Resuming after restart", text);
        Assert.Equal(1, text.Split("Configure Windows", StringSplitOptions.None).Length - 1);
        Assert.Contains("Failed", text);
        Assert.Contains("42", text);
        Assert.Contains("warnings", text);
        Assert.Contains("Succeeded: 1  Failed: 1  Skipped: 0", text);
        Assert.Contains("test.log", text);
    }

    [Fact]
    public void RedirectedOutput_ShowsCountdownWithoutInjectingConsoleControls()
    {
        using var output = new StringWriter();
        using var console = new PostInstallConsole(new(), "test.log", output);
        console.Report(new([new("a", "A\r\nB\u001b[2J", "Succeeded")], "AwaitingRestart", RestartSecondsRemaining: 2));
        console.Report(new([], "AwaitingRestart", RestartSecondsRemaining: 0));
        string text = output.ToString();
        Assert.DoesNotContain('\u001b', text);
        Assert.DoesNotContain("A\r\nB", text);
        Assert.Contains("Restarting in 2 seconds", text);
        Assert.Contains("Restarting now", text);
    }

    [Fact]
    public void BrokenOutput_DoesNotInterruptDeployment()
    {
        using var console = new PostInstallConsole(new(), "test.log", new BrokenWriter());
        console.Report(new([new("a", "Install software", "Running")], "Running"));
        console.Complete(new("Succeeded", 0));
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("Console unavailable");
    }

    [Theory]
    [InlineData("Succeeded", 0, 10)]
    [InlineData("CompletedWithErrors", 0, 10)]
    [InlineData("AwaitingRestart", 2, 0)]
    [InlineData("Failed", 3, 0)]
    public async Task FinalHandoff_WaitsOnlyAfterCompletionAndPreservesResult(string status, int exitCode, int expectedDelayCount)
    {
        using var output = new StringWriter();
        using var console = new PostInstallConsole(new(), "test.log", output);
        var delays = new List<TimeSpan>();
        Task Delay(TimeSpan value) { delays.Add(value); return Task.CompletedTask; }
        console.Complete(new(status, exitCode));
        await console.WaitForSetupAsync(Delay);
        Assert.Equal(expectedDelayCount, delays.Count);
        Assert.All(delays, value => Assert.Equal(TimeSpan.FromSeconds(1), value));
        if (expectedDelayCount > 0)
        {
            Assert.Contains("completed", output.ToString());
            Assert.Contains("Continuing Windows Setup", output.ToString());
        }
        else Assert.DoesNotContain("Continuing Windows Setup", output.ToString());
    }
}
