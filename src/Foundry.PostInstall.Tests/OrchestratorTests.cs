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
        public PreOobeExecutionPlan Plan { get; }
        public string Hash { get; }
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
                    CustomAction = new PreOobeActionSettings
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

        public async Task<int> Run(string boot) => (await new PreOobeOrchestrator(
            Root, Hash, Journal, this, () => boot).RunAsync(Plan, TestContext.Current.CancellationToken)).ExitCode;

        public Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken)
        {
            Executed.Add(action.Id);
            return Task.FromResult(action.Id == "first" && Outcome is not null ? Outcome : new ActionStepOutcome(true));
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
