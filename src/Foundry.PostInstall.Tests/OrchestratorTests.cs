// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class OrchestratorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task RestartCountdown_CommitsBeforeWaitingAndResumesAtNextAction(int seconds)
    {
        using var fixture = new Fixture("first", "restart", "last");
        fixture.SetRestartDelay(seconds);
        var progress = new ProgressRecorder();
        int waits = 0;
        Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            Assert.Equal(TimeSpan.FromSeconds(1), duration);
            var checkpoint = fixture.Journal.Read();
            Assert.Equal("AwaitingRestart", checkpoint.Status);
            Assert.Equal(2, checkpoint.Cursor);
            Assert.Equal(1, checkpoint.RestartCount);
            Assert.Equal("Succeeded", checkpoint.Actions["restart"].Status);
            Assert.Equal(["first"], fixture.Executed);
            waits++;
            return Task.CompletedTask;
        }

        Assert.Equal(2, await fixture.Run("boot-one", progress, Delay));
        Assert.Equal(seconds, waits);
        Assert.Equal(Enumerable.Range(0, seconds + 1).Reverse(), progress.Values
            .Where(value => value.RestartSecondsRemaining.HasValue).Select(value => value.RestartSecondsRemaining!.Value));
        Assert.Equal(0, await fixture.Run("boot-two", progress, Delay));
        Assert.Equal(["first", "last"], fixture.Executed);
        Assert.Equal(seconds, waits);
        Assert.Contains(progress.Values, value => value.IsResuming && value.Actions[0].Status == "Succeeded");
        Assert.Equal("Succeeded", progress.Values[^1].Status);
    }

    [Fact]
    public async Task CancelledCountdown_PreservesCheckpointWithoutRunningNextAction()
    {
        using var fixture = new Fixture("restart", "last");
        fixture.SetRestartDelay(3);
        using var cancellation = new CancellationTokenSource();
        Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellationToken);
        }
        var outcome = await new PreOobeOrchestrator(fixture.Root, fixture.Hash, fixture.Journal,
            fixture, () => "boot-one", delay: Delay).RunAsync(fixture.Plan, cancellation.Token);
        Assert.Equal(3, outcome.ExitCode);
        Assert.Equal("AwaitingRestart", fixture.Journal.Read().Status);
        Assert.Empty(fixture.Executed);
        Assert.Equal(0, await fixture.Run("boot-two"));
        Assert.Equal(["last"], fixture.Executed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(86401)]
    public async Task InvalidRestartDelay_IsRejectedBeforeAnyAction(int seconds)
    {
        using var fixture = new Fixture("first", "restart");
        fixture.SetRestartDelay(seconds);
        Assert.Equal(3, await fixture.Run("boot"));
        Assert.Empty(fixture.Executed);
    }

    [Fact]
    public async Task Progress_ReportsFailureContinuationCleanupAndFinalOutcome()
    {
        using var fixture = new Fixture("first", "last", "cleanup");
        fixture.Outcome = new(false, 42, "test_failure");
        var progress = new ProgressRecorder();
        Assert.Equal(0, await fixture.Run("boot", progress));
        Assert.Contains(progress.Values, value => value.Actions[0].Status == "Running");
        Assert.Contains(progress.Values, value => value.Actions[0].Status == "Failed" && value.Actions[0].ExitCode == 42);
        Assert.Contains(progress.Values, value => value.Actions[^1].Status == "Running");
        Assert.Equal("CompletedWithErrors", progress.Values[^1].Status);
        Assert.Equal("Succeeded", progress.Values[^1].Actions[^1].Status);
        Assert.Equal("Cleanup", progress.Values[^1].Actions[^1].Name);
    }

    [Fact]
    public async Task BrokenProgressOutput_DoesNotChangeExecution()
    {
        using var fixture = new Fixture("first", "last");
        Assert.Equal(0, await fixture.Run("boot", new ProgressRecorder { Fail = true }));
        Assert.Equal(["first", "last"], fixture.Executed);
    }

    [Fact]
    public async Task PlannedRestarts_ResumeWithoutReplayingCompletedActions()
    {
        using var fixture = new Fixture("first", "restart", "second", "restart2", "last");
        Assert.Equal(2, await fixture.Run("boot-one"));
        Assert.Equal(["first"], fixture.Executed);
        Assert.Equal(3, await fixture.Run("boot-one"));
        Assert.Equal(2, await fixture.Run("boot-two"));
        Assert.Equal(["first", "second"], fixture.Executed);
        Assert.Equal(0, await fixture.Run("boot-three"));
        Assert.Equal(0, await fixture.Run("boot-three"));
        Assert.Equal(["first", "second", "last"], fixture.Executed);
    }

    [Fact]
    public async Task MissingJournal_NeverStartsActions()
    {
        using var fixture = new Fixture("first");
        File.Delete(fixture.Journal.Path);
        Assert.Equal(3, await fixture.Run("boot"));
        Assert.Empty(fixture.Executed);
    }

    [Fact]
    public async Task InterruptedRunningAction_IsNotReplayed()
    {
        using var fixture = new Fixture("first");
        var journal = fixture.Journal.Read();
        journal.Status = "Running";
        journal.BootIdentity = "old-boot";
        fixture.Journal.Write(journal);
        Assert.Equal(3, await fixture.Run("new-boot"));
        Assert.Empty(fixture.Executed);
    }

    [Theory]
    [InlineData(false, 0, "CompletedWithErrors")]
    [InlineData(true, 3, "Failed")]
    public async Task Continue_OnlyAppliesToObservedOrdinaryFailure(bool uncertain, int expectedCode, string expectedStatus)
    {
        using var fixture = new Fixture("first", "second");
        fixture.Outcome = new ActionStepOutcome(false, 7, "test_failure", TerminationUncertain: uncertain);
        Assert.Equal(expectedCode, await fixture.Run("boot"));
        Assert.Equal(expectedStatus, fixture.Journal.Read().Status);
        Assert.Equal(uncertain ? ["first"] : new[] { "first", "second" }, fixture.Executed);
    }

    [Fact]
    public async Task LeaseConflict_DoesNotDisposeAnotherInvocationPayload()
    {
        using var fixture = new Fixture("first");
        using IDisposable lease = fixture.Journal.AcquireLease();
        Assert.Equal(3, await fixture.Run("boot"));
        Assert.Equal("Pending", fixture.Journal.Read().Status);
        Assert.Empty(fixture.Executed);
    }

    private sealed class Fixture : IDisposable, IPreOobeActionExecutor
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));
        public List<string> Executed { get; } = [];
        public ExecutionJournal Journal { get; }
        public PreOobeExecutionPlan Plan { get; private set; }
        public string Hash { get; private set; }
        public ActionStepOutcome? Outcome { get; set; }

        public Fixture(params string[] ids)
        {
            Directory.CreateDirectory(Root);
            Plan = new PreOobeExecutionPlan
            {
                OperationId = Guid.NewGuid().ToString("N"),
                AttemptId = Guid.NewGuid().ToString("N"),
                Actions = ids.Select(id => new PreOobeExecutionAction
                {
                    Id = id,
                    Name = id,
                    BuiltInKind = id == "cleanup" ? PreOobeBuiltInKind.Cleanup : null,
                    CustomAction = id == "cleanup" ? null : new PreOobeActionSettings
                    {
                        Id = id,
                        Name = id,
                        Kind = id.StartsWith("restart", StringComparison.Ordinal) ? PreOobeActionKind.Restart : PreOobeActionKind.Command,
                        Command = "unused fixture command",
                        Process = new() { ErrorPolicy = PreOobeErrorPolicy.Continue }
                    }
                }).ToArray()
            };
            Hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Plan)));
            Journal = new ExecutionJournal(Root);
            Journal.Seed(Plan, Hash);
        }

        public void SetRestartDelay(int seconds)
        {
            Plan = Plan with
            {
                Actions = Plan.Actions.Select(action => action.CustomAction?.Kind == PreOobeActionKind.Restart
                ? action with { CustomAction = action.CustomAction with { RestartDelaySeconds = seconds } } : action).ToArray()
            };
            Hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Plan)));
            var state = Journal.Read();
            state.PlanHash = Hash;
            Journal.Write(state);
        }

        public async Task<int> Run(string boot, IProgress<PostInstallProgress>? progress = null,
            Func<TimeSpan, CancellationToken, Task>? delay = null) => (await new PreOobeOrchestrator(
            Root, Hash, Journal, this, () => boot, progress, delay).RunAsync(Plan, TestContext.Current.CancellationToken)).ExitCode;

        public Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken)
        {
            Executed.Add(action.Id);
            return Task.FromResult(action.Id == "first" && Outcome is not null ? Outcome : new ActionStepOutcome(true));
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class ProgressRecorder : IProgress<PostInstallProgress>
    {
        public List<PostInstallProgress> Values { get; } = [];
        public bool Fail { get; init; }
        public void Report(PostInstallProgress value)
        {
            if (Fail) throw new IOException("Console unavailable.");
            Values.Add(value);
        }
    }
}
