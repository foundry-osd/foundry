// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
using Foundry.PostInstall.Execution;
namespace Foundry.PostInstall.Tests;

public sealed class DomainJoinActionTests
{
    [Fact]
    public async Task JoinAndFailedPlacement_StillRequestsRestartAndWritesSeparateReport()
    {
        using var f = new DomainFixture();
        f.Directory.Existing = f.Directory.Computer; f.Directory.StaleGuid = true;
        new DomainJoinResultStore(f.Root, f.Plan, f.Hash).Seed();
        var processes = new WorkerProcess(f);
        var result = await new DomainJoinAction(f.Root, f.Plan, f.Hash, "installed-boot", processes, "runtime.exe").ExecuteAsync(f.Plan.Actions[0], CancellationToken.None);
        Assert.False(result.Succeeded); Assert.True(result.RestartRequested); Assert.True(result.HasWarnings);
        var report = new DomainJoinResultStore(f.Root, f.Plan, f.Hash).Read();
        Assert.Equal(DomainJoinPhaseState.Succeeded, report.Join.State);
        Assert.Equal(DomainJoinPhaseState.Failed, report.Placement.State);
        Assert.Equal(["--domain-join-worker"], processes.Command!.Arguments);
        Assert.Null(processes.Command.OutputPath);
        Assert.Equal(DomainJoinAction.SupervisionTimeout, processes.Command.Timeout);
        Assert.True(DomainJoinAction.SupervisionTimeout > Foundry.PostInstall.Windows.DomainJoinWorker.Budget);
    }
    [Fact]
    public async Task TimeoutWithJoinStarted_ReportsUnknownAndCannotReplay()
    {
        using var f = new DomainFixture(); new DomainJoinResultStore(f.Root, f.Plan, f.Hash).Seed();
        var processes = new WorkerProcess(f) { Interrupt = true };
        var action = new DomainJoinAction(f.Root, f.Plan, f.Hash, "installed-boot", processes, "runtime.exe");
        var outcome = await action.ExecuteAsync(f.Plan.Actions[0], CancellationToken.None);
        Assert.True(outcome.TerminationUncertain); Assert.True(outcome.HasWarnings);
        Assert.Equal(DomainJoinPhaseState.Unknown, new DomainJoinResultStore(f.Root, f.Plan, f.Hash).Read().Join.State);
        await Assert.ThrowsAsync<InvalidDataException>(() => action.ExecuteAsync(f.Plan.Actions[0], CancellationToken.None));
        Assert.Equal(1, processes.Starts);
    }
    [Theory]
    [InlineData("installed-boot", "PC-01", false)]
    [InlineData("next-boot", "OLD-NAME", false)]
    [InlineData("next-boot", "PC-01", true)]
    public async Task Verification_RequiresLaterBootAndExpectedActiveName(string boot, string name, bool success)
    {
        using var f = new DomainFixture();
        var report = new DomainJoinResultStore(f.Root, f.Plan, f.Hash); report.Seed();
        report.Write(report.Read() with { OriginatingBootId = "installed-boot", Join = new() { State = DomainJoinPhaseState.Succeeded }, Restart = DomainJoinRestartState.Required });
        var journal = new ExecutionJournal(f.Root); var state = journal.Read(); state.Cursor = 1; state.BootIdentity = boot;
        state.Actions["verify"] = new() { Status = "Running" }; journal.Write(state);
        File.Delete(OwnedPaths.Resolve(f.Root, f.Parameters.CredentialPayloadPath)); f.Native.Name = name;
        var outcome = await new DomainMembershipVerificationAction(f.Root, f.Plan, f.Hash, boot, f.Native).ExecuteAsync(f.Plan.Actions[1], CancellationToken.None);
        Assert.Equal(success, outcome.Succeeded); Assert.Equal(!success, outcome.HasWarnings);
        Assert.Equal(success, report.Read().Membership.State == DomainJoinPhaseState.Succeeded);
    }
    private sealed class WorkerProcess(DomainFixture fixture) : IPreOobeProcessExecutor
    {
        public ProcessCommand? Command;
        public bool Interrupt;
        public int Starts;
        public async Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken token)
        {
            Starts++; Command = command;
            if (Interrupt)
            {
                var store = new DomainJoinPhaseStore(fixture.Root, fixture.Plan, fixture.Hash);
                var bound = store.BindOrigin("installed-boot"); store.Write(bound with { Generation = 2, Phase = DomainJoinReceiptPhase.JoinStarted });
                return new(null, "untrusted stdout", TimedOut: true, TerminationUncertain: true);
            }
            await fixture.Run(); return new(0, "untrusted stdout");
        }
    }
}
