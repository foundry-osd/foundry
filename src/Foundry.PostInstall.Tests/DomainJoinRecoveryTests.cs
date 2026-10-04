// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Actions;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class DomainJoinRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPlacement_RestartsThenVerifiesAndContinues(bool aggregateFailure)
    {
        using var f = new RecoveryFixture { AggregateJoinFailure = aggregateFailure };
        f.Domain.Directory.Existing = f.Domain.Directory.Computer;
        f.Domain.Directory.StaleGuid = true;
        Assert.Equal(2, await f.Run());
        Assert.Equal(DomainJoinRestartState.Requested, f.Report.Read().Restart);
        Assert.Equal(3, await f.Run());
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.Equal("CompletedWithErrors", f.Journal.Read().Status);
        Assert.Equal(DomainJoinPhaseState.Succeeded, f.Report.Read().Membership.State);
        Assert.Equal(["join", "verify", "custom", "cleanup"], f.Executed);
        Assert.Equal(1, f.Domain.Native.Joins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedJoinOrWorkerStart_ContinuesWithoutRestartOrMembershipQuery(bool startFailure)
    {
        using var f = new RecoveryFixture { StartFailure = startFailure };
        f.Domain.Native.JoinCode = 5;
        Assert.Equal(0, await f.Run());
        Assert.Equal("CompletedWithErrors", f.Journal.Read().Status);
        Assert.Equal(0, f.Journal.Read().RestartCount);
        Assert.Equal(DomainJoinPhaseState.Failed, f.Report.Read().Join.State);
        Assert.Equal(DomainJoinPhaseState.Skipped, f.Report.Read().Membership.State);
        Assert.Equal(DomainJoinRestartState.NotRequired, f.Report.Read().Restart);
        Assert.Contains("custom", f.Executed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownJoin_RequestsOneRestartAndKeepsUnknownAfterMatchingMembership(bool liveWorker)
    {
        using var f = new RecoveryFixture { Interrupt = true, LiveWorker = liveWorker };
        Assert.Equal(2, await f.Run());
        Assert.Equal(3, await f.Run());
        Assert.Equal(1, f.Journal.Read().RestartCount);
        if (liveWorker) Assert.True(File.Exists(f.Credentials));
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.Equal(DomainJoinPhaseState.Unknown, f.Report.Read().Join.State);
        Assert.Equal(DomainJoinPhaseState.Succeeded, f.Report.Read().Membership.State);
        Assert.Equal(DomainJoinRestartState.Completed, f.Report.Read().Restart);
        Assert.False(File.Exists(f.Credentials));
        Assert.Equal(1, f.Executed.Count(id => id == "join"));
    }

    [Theory]
    [InlineData(DomainJoinReceiptPhase.Prepared, false, "Running", 0)]
    [InlineData(DomainJoinReceiptPhase.JoinStarted, false, "Running", 2)]
    [InlineData(DomainJoinReceiptPhase.JoinReturned, false, "Running", 2)]
    [InlineData(DomainJoinReceiptPhase.PlacementStarted, false, "Running", 2)]
    [InlineData(DomainJoinReceiptPhase.Finished, true, "Running", 2)]
    [InlineData(DomainJoinReceiptPhase.Finished, true, "Completing", 2)]
    [InlineData(DomainJoinReceiptPhase.Finished, true, "CompletedWithErrors", 2)]
    [InlineData(DomainJoinReceiptPhase.Finished, true, "Failed", 2)]
    public async Task Recovery_ReconcilesBeforeDispatchAndNeverReplays(DomainJoinReceiptPhase phase, bool advanced, string status, int firstExit)
    {
        using var f = new RecoveryFixture();
        f.PrepareCrash(phase, advanced, status);
        Assert.Equal(firstExit, await f.Run());
        if (firstExit == 2)
        {
            Assert.Equal(3, await f.Run());
            Assert.DoesNotContain("verify", f.Executed);
            Assert.Equal(0, await f.Run("next-boot"));
        }
        Assert.DoesNotContain("join", f.Executed);
        Assert.Contains("custom", f.Executed);
        Assert.Equal(0, f.Domain.Native.Joins);
        Assert.Equal(0, f.Domain.Directory.Moves);
        if (phase == DomainJoinReceiptPhase.JoinStarted) Assert.Equal(DomainJoinPhaseState.Unknown, f.Report.Read().Join.State);
        if (phase == DomainJoinReceiptPhase.PlacementStarted) Assert.Equal(DomainJoinPhaseState.Unknown, f.Report.Read().Placement.State);
    }

    [Theory]
    [InlineData("operationId")]
    [InlineData("attemptId")]
    [InlineData("planHash")]
    [InlineData("actionId")]
    [InlineData("originatingBootId")]
    [InlineData("torn")]
    public async Task InvalidReceipt_IsRejectedBeforeContinuation(string field)
    {
        using var f = new RecoveryFixture();
        f.PrepareCrash(DomainJoinReceiptPhase.JoinStarted, false, "Running");
        string path = Path.Combine(f.Domain.Root, "State", "PreOobe", "domain-join-phase.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json[field] = "foreign";
        File.WriteAllText(path, field == "torn" ? "{" : json.ToJsonString());
        Assert.Equal(3, await f.Run());
        Assert.Empty(f.Executed);
        Assert.Equal(0, f.Journal.Read().RestartCount);
    }

    [Fact]
    public async Task LockedMalformedDomainPayload_RemainsWarningThroughCustomAndFinalCleanupThenRetriesNextBoot()
    {
        using var f = new RecoveryFixture { StartFailure = true };
        File.WriteAllText(f.Credentials, "malformed");
        using (var locked = new FileStream(f.Credentials, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(0, await f.Run());
            Assert.True(f.PendingAtCustom);
            Assert.Equal("CompletedWithErrors", f.Journal.Read().Status);
            Assert.Equal("CleanupPending", f.Journal.Read().PayloadDispositions[f.Domain.Parameters.CredentialPayloadPath]);
            Assert.Equal(DomainJoinCleanupState.Pending, f.Report.Read().Cleanup);
        }
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.False(File.Exists(f.Credentials));
        Assert.Equal(DomainJoinCleanupState.Disposed, f.Report.Read().Cleanup);
        Assert.Equal(1, f.Executed.Count(id => id == "custom"));
    }

    [Fact]
    public async Task DomainStagingRecords_NeverArmMutation()
    {
        using var f = new RecoveryFixture();
        var state = f.Journal.Read(); state.Status = "Staging"; f.Journal.Write(state);
        Assert.Equal(3, await f.Run());
        Assert.Empty(f.Executed);
        Assert.True(File.Exists(f.Credentials));
        Assert.Equal("Staging", f.Journal.Read().Status);
    }

    [Theory]
    [InlineData("before-checkpoint", 2)]
    [InlineData("after-checkpoint", 3)]
    [InlineData("cursor", 2)]
    public async Task RestartPublicationCrash_RecoversWithoutSecondMutationOrDuplicateRestart(string fault, int nextExit)
    {
        using var f = new RecoveryFixture(fault);
        Assert.Equal(3, await f.Run());
        Assert.Equal(nextExit, await f.Run());
        Assert.Equal(3, await f.Run());
        Assert.Equal(1, f.Journal.Read().RestartCount);
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.Equal(1, f.Domain.Native.Joins);
        Assert.Equal(1, f.Executed.Count(id => id == "verify"));
    }

    [Fact]
    public async Task CorruptReceiptDuringRunningWork_DoesNotDeletePossiblyLiveCredentials()
    {
        using var f = new RecoveryFixture();
        f.PrepareCrash(DomainJoinReceiptPhase.JoinStarted, false, "Running");
        File.WriteAllText(Path.Combine(f.Domain.Root, "State", "PreOobe", "domain-join-phase.json"), "{");
        Assert.Equal(3, await f.Run());
        Assert.True(File.Exists(f.Credentials));
    }

    [Fact]
    public async Task UnrelatedUncertainCustomAction_RemainsFatalAfterDomainWarning()
    {
        using var f = new RecoveryFixture { StartFailure = true, UncertainCustom = true };
        Assert.Equal(3, await f.Run());
        Assert.Equal("Failed", f.Journal.Read().Status);
        Assert.Equal(3, await f.Run("next-boot"));
        Assert.Equal(1, f.Executed.Count(id => id == "custom"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupJournalWriteFailure_RemainsFatalEvenForDomainOwnedPayload(bool finalGate)
    {
        using var f = new RecoveryFixture(finalGate ? "cleanup-final" : "cleanup-immediate") { StartFailure = true };
        Assert.Equal(3, await f.Run());
        Assert.Equal("Failed", f.Journal.Read().Status);
        Assert.False(File.Exists(f.Credentials));
    }

    [Fact]
    public async Task RolledBackValidReceipt_IsRejectedAfterRestartPublication()
    {
        using var f = new RecoveryFixture();
        string path = Path.Combine(f.Domain.Root, "State", "PreOobe", "domain-join-phase.json");
        byte[] seed = File.ReadAllBytes(path);
        Assert.Equal(2, await f.Run());
        File.WriteAllBytes(path, seed);
        Assert.Equal(3, await f.Run("next-boot"));
        Assert.DoesNotContain("verify", f.Executed);
    }

    [Fact]
    public async Task ReportWriteFailureAfterCheckpoint_NeverConsumesRestartTwice()
    {
        using var f = new RecoveryFixture("report-after-checkpoint");
        Assert.Equal(3, await f.Run());
        f.ReleaseReportLock();
        Assert.Equal(3, await f.Run());
        Assert.Equal(1, f.Journal.Read().RestartCount);
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.Equal(1, f.Domain.Native.Joins);
    }

    [Fact]
    public async Task UnsettledWorkerWithPreparedReceipt_RequiresConservativeRestart()
    {
        using var f = new RecoveryFixture { InterruptBeforeStart = true };
        Assert.Equal(2, await f.Run());
        Assert.True(File.Exists(f.Credentials));
        Assert.Equal(DomainJoinPhaseState.Unknown, f.Report.Read().Join.State);
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.Equal(DomainJoinPhaseState.Unknown, f.Report.Read().Join.State);
    }

    [Fact]
    public async Task RecoveryWhilePreparedWorkerLeaseIsHeld_DoesNotAssumeMutationWasPrevented()
    {
        using var f = new RecoveryFixture();
        f.PrepareCrash(DomainJoinReceiptPhase.Prepared, false, "Running");
        using var lease = new DomainJoinPhaseStore(f.Domain.Root, f.Domain.Plan, f.Domain.Hash).AcquireWorkerLease();
        Assert.Equal(2, await f.Run());
        Assert.True(File.Exists(f.Credentials));
        Assert.Equal(DomainJoinPhaseState.Unknown, f.Report.Read().Join.State);
    }

    [Fact]
    public async Task LostNativeReturn_ContinuesAfterRestartWithoutRepeatingJoin()
    {
        using var f = new RecoveryFixture();
        f.Domain.Native.ThrowOnJoin = true;
        Assert.Equal(2, await f.Run());
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.Equal(1, f.Domain.Native.Joins);
        Assert.Equal(DomainJoinPhaseState.Unknown, f.Report.Read().Join.State);
        Assert.Equal(DomainJoinPhaseState.Succeeded, f.Report.Read().Membership.State);
    }

    [Fact]
    public async Task TerminalSkippedUnstartedJoin_DoesNotResumeAbortedDeployment()
    {
        using var f = new RecoveryFixture();
        var state = f.Journal.Read(); state.Status = "Failed"; state.BootIdentity = "installed-boot";
        state.Actions["join"] = new() { Status = "Skipped" }; f.Journal.Write(state);
        Assert.Equal(3, await f.Run());
        Assert.Equal(["cleanup"], f.Executed);
        Assert.Equal(DomainJoinPhaseState.NotStarted, f.Report.Read().Join.State);
        Assert.Equal(0, f.Journal.Read().RestartCount);
    }

    [Fact]
    public async Task JoinReturnWithoutOu_DoesNotInventPlacementWarning()
    {
        using var f = new RecoveryFixture(targetOu: false);
        f.PrepareCrash(DomainJoinReceiptPhase.JoinReturned, false, "Running");
        Assert.Equal(2, await f.Run());
        Assert.Equal(0, await f.Run("next-boot"));
        Assert.Equal(DomainJoinPhaseState.Skipped, f.Report.Read().Placement.State);
        Assert.Equal("Succeeded", f.Journal.Read().Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DomainCleanup_RetriesBoundedlyAndClassifiesWithoutDecoding(bool release)
    {
        using var f = new RecoveryFixture();
        File.WriteAllText(f.Credentials, "malformed");
        using var locked = new FileStream(f.Credentials, FileMode.Open, FileAccess.Read, FileShare.None);
        var waits = new List<TimeSpan>();
        var cleanup = new OwnedPayloadCleanup(f.Domain.Root, f.Journal, duration =>
        {
            waits.Add(duration);
            if (release && waits.Count == 2) locked.Dispose();
        });
        var outcome = cleanup.Dispose(f.Domain.Plan, f.Journal.Read(), true, "installed-boot");
        Assert.False(outcome.HasFatalSensitiveFailure);
        Assert.Equal(!release, outcome.HasDomainCleanupPending);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250) }, waits);
        Assert.Equal(!release, File.Exists(f.Credentials));
    }

    private sealed class RecoveryFixture : IDisposable, IPreOobeActionExecutor, IPreOobeProcessExecutor
    {
        public DomainFixture Domain { get; }
        public ExecutionJournal Journal { get; }
        public DomainJoinResultStore Report { get; }
        public string Credentials => OwnedPaths.Resolve(Domain.Root, Domain.Parameters.CredentialPayloadPath);
        public List<string> Executed { get; } = [];
        public bool StartFailure;
        public bool Interrupt;
        public bool InterruptBeforeStart;
        public bool LiveWorker;
        public bool PendingAtCustom;
        public bool UncertainCustom;
        public bool AggregateJoinFailure;
        private string boot = "installed-boot";
        public RecoveryFixture(string? fault = null, bool targetOu = true)
        {
            Domain = new(targetOu, configure: plan => plan with
            {
                Actions = [.. plan.Actions,
                new() { Id = "custom", CustomAction = new() { Kind = PreOobeActionKind.Command, Process = new() } },
                new() { Id = "cleanup", BuiltInKind = PreOobeBuiltInKind.Cleanup }]
            });
            Journal = fault is null ? new ExecutionJournal(Domain.Root) : new FaultJournal(Domain.Root, fault);
            Journal.Write(ExecutionJournal.CreateSeed(Domain.Plan, Domain.Hash));
            Report = new(Domain.Root, Domain.Plan, Domain.Hash); Report.Seed();
        }
        public async Task<int> Run(string currentBoot = "installed-boot")
        {
            boot = currentBoot;
            return (await new PreOobeOrchestrator(Domain.Root, Domain.Hash, Journal, this, () => boot)
                .RunAsync(Domain.Plan, TestContext.Current.CancellationToken)).ExitCode;
        }
        public async Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken token)
        {
            Executed.Add(action.Id);
            if (action.Id == "join")
            {
                var result = await new DomainJoinAction(Domain.Root, Domain.Plan, Domain.Hash, boot, this, "runtime.exe").ExecuteAsync(action, token);
                return AggregateJoinFailure ? result with { Succeeded = false } : result;
            }
            if (action.Id == "verify") return await new DomainMembershipVerificationAction(Domain.Root, Domain.Plan, Domain.Hash, boot, Domain.Native).ExecuteAsync(action, token);
            if (action.Id == "custom")
            {
                PendingAtCustom = Report.Read().Cleanup == DomainJoinCleanupState.Pending;
                if (UncertainCustom) return new(false, TerminationUncertain: true);
            }
            return new(true);
        }
        public async Task<ProcessOutcome> RunAsync(ProcessCommand command, CancellationToken token)
        {
            if (StartFailure) throw new System.ComponentModel.Win32Exception(2);
            if (InterruptBeforeStart) return new(null, "", TerminationUncertain: true);
            if (Interrupt)
            {
                var store = new DomainJoinPhaseStore(Domain.Root, Domain.Plan, Domain.Hash);
                var receipt = store.BindOrigin(boot);
                store.Write(receipt with { Generation = 2, Phase = DomainJoinReceiptPhase.JoinStarted });
                return new(null, "", TimedOut: true, TerminationUncertain: LiveWorker);
            }
            await Domain.Run(); return new(0, "");
        }
        public void PrepareCrash(DomainJoinReceiptPhase phase, bool advanced, string status)
        {
            var state = Journal.Read(); state.Status = "Running"; state.BootIdentity = boot;
            state.Actions["join"] = new() { Status = "Running" }; Journal.Write(state);
            var store = new DomainJoinPhaseStore(Domain.Root, Domain.Plan, Domain.Hash);
            var receipt = store.BindOrigin(boot);
            if (phase >= DomainJoinReceiptPhase.JoinStarted) store.Write(receipt = receipt with { Generation = 2, Phase = DomainJoinReceiptPhase.JoinStarted });
            if (phase >= DomainJoinReceiptPhase.JoinReturned) store.Write(receipt = receipt with
            {
                Generation = 3,
                Phase = DomainJoinReceiptPhase.JoinReturned,
                Join = new() { State = DomainJoinPhaseState.Succeeded },
                RestartRequired = true
            });
            if (phase == DomainJoinReceiptPhase.PlacementStarted) store.Write(receipt with
            {
                Generation = 4,
                Phase = phase,
                ComputerObjectGuid = Domain.Directory.Computer.Guid,
                DestinationObjectGuid = Domain.Directory.Target.Guid
            });
            if (phase == DomainJoinReceiptPhase.Finished) store.Write(receipt with
            {
                Generation = 4,
                Phase = phase,
                Placement = new() { State = DomainJoinPhaseState.Failed, FailureCode = DomainJoinFailureCode.Interrupted }
            });
            state.Status = status; state.CompletionStatus = status == "Completing" ? "CompletedWithErrors" : null;
            if (advanced) { state.Cursor = 1; state.Actions["join"] = new() { Status = "Failed" }; }
            Journal.Write(state);
        }
        public void Dispose() { ReleaseReportLock(); Domain.Dispose(); }
        public void ReleaseReportLock() => (Journal as FaultJournal)?.ReportLock?.Dispose();
    }

    private sealed class FaultJournal(string root, string fault) : ExecutionJournal(root)
    {
        private bool failed;
        public FileStream? ReportLock;
        public override void Write(JournalState state)
        {
            bool trigger = fault switch
            {
                "before-checkpoint" or "after-checkpoint" => state.Status == "AwaitingRestart",
                "report-after-checkpoint" => state.Status == "AwaitingRestart",
                "cursor" => state.Status == "Running" && state.Cursor == 1,
                "cleanup-immediate" => state.PayloadDispositions.Values.Contains("CleanupPending"),
                "cleanup-final" => state.Status == "Completing",
                _ => false
            };
            if (!failed && trigger)
            {
                failed = true;
                if (fault == "report-after-checkpoint")
                {
                    base.Write(state);
                    ReportLock = new FileStream(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "domain-join-result.json"), FileMode.Open, FileAccess.Read, FileShare.None);
                    return;
                }
                if (fault == "after-checkpoint") base.Write(state);
                throw new IOException("Injected journal publication failure.");
            }
            base.Write(state);
        }
    }
}
