// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class DomainPlanBindingTests
{
    [Fact]
    public async Task CapabilityTwo_PreservesLegacyExecution()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var plan = new PreOobeExecutionPlan { OperationId = "operation", AttemptId = "attempt", RuntimeContractVersion = 2 };
            var journal = new ExecutionJournal(root);
            journal.Seed(plan, new string('a', 64));
            var outcome = await new PreOobeOrchestrator(root, new string('a', 64), journal, new Executor(), () => "boot")
                .RunAsync(plan, CancellationToken.None);
            Assert.Equal(0, outcome.ExitCode);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("capability")]
    [InlineData("consumer")]
    [InlineData("verification")]
    [InlineData("payload")]
    [InlineData("attempt")]
    public void DomainBinding_RejectsUnsafePairOrCredentialOwnership(string fault)
    {
        using var f = new DomainFixture();
        var plan = fault switch
        {
            "capability" => f.Plan with { RuntimeContractVersion = 1 },
            "consumer" => f.Plan with { OwnedPayloads = [f.Plan.OwnedPayloads[0] with { ConsumerActionIds = ["join", "verify"] }] },
            "verification" => f.Plan with { Actions = [f.Plan.Actions[0]] },
            "payload" => f.Plan with { OwnedPayloads = [f.Plan.OwnedPayloads[0] with { RelativePath = "Payloads/wrong.bin" }] },
            _ => f.Plan with { AttemptId = "not-an-attempt" }
        };
        Assert.Throws<InvalidDataException>(() => PreOobePlanValidator.ValidatePlan(plan));
    }
    [Theory]
    [InlineData("boot")]
    [InlineData("cursor")]
    [InlineData("hash")]
    [InlineData("operation")]
    [InlineData("attempt")]
    [InlineData("status")]
    public async Task Worker_RejectsJournalMismatchBeforeAnyAuthentication(string fault)
    {
        using var f = new DomainFixture();
        var journal = new ExecutionJournal(f.Root); var state = journal.Read();
        switch (fault)
        {
            case "boot": state.BootIdentity = "other"; break;
            case "cursor": state.Cursor = 1; break;
            case "hash": state.PlanHash = new string('b', 64); break;
            case "operation": state.OperationId = Guid.NewGuid().ToString("N"); break;
            case "attempt": state.AttemptId = Guid.NewGuid().ToString("N"); break;
            case "status": state.Status = "Pending"; break;
        }
        journal.Write(state);
        await Assert.ThrowsAsync<InvalidDataException>(f.Run);
        Assert.Equal(0, f.Directory.Prepares); Assert.Equal(0, f.Native.Joins);
    }
    [Fact]
    public async Task Loader_HashesExactSnapshotAndRejectsOversizedFiles()
    {
        using var f = new DomainFixture();
        var snapshot = await PreOobePlanLoader.LoadAsync(f.Root, CancellationToken.None);
        Assert.Equal(f.Hash, snapshot.Hash);
        string path = Path.Combine(f.Root, "State", "PreOobe", "plan.json");
        await File.AppendAllTextAsync(path, " ", TestContext.Current.CancellationToken);
        snapshot = await PreOobePlanLoader.LoadAsync(f.Root, CancellationToken.None);
        Assert.NotEqual(f.Hash, snapshot.Hash);
        await Assert.ThrowsAsync<InvalidDataException>(f.Run);
        using (var file = File.OpenWrite(path)) file.SetLength(8 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => PreOobePlanLoader.LoadAsync(f.Root, CancellationToken.None));
    }
    [Fact]
    public async Task Worker_DoesNotAcquireParentsRunnerLease()
    {
        using var f = new DomainFixture();
        using var lease = new ExecutionJournal(f.Root).AcquireLease();
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Succeeded, result.Join.State);
    }
    [Theory]
    [InlineData("nonadjacent")]
    [InlineData("network-after")]
    [InlineData("overlapping-ownership")]
    public void RecoveryBinding_RejectsAmbiguousSequenceAndPayloadOwnership(string fault)
    {
        using var f = new DomainFixture();
        var plan = fault switch
        {
            "nonadjacent" => f.Plan with { Actions = [f.Plan.Actions[0], new() { Id = "appx", BuiltInKind = PreOobeBuiltInKind.Appx }, f.Plan.Actions[1]] },
            "network-after" => f.Plan with { Actions = [.. f.Plan.Actions, new() { Id = "network", BuiltInKind = PreOobeBuiltInKind.Network }] },
            _ => f.Plan with { OwnedPayloads = [.. f.Plan.OwnedPayloads, new() { RelativePath = "Payloads/DomainJoin", IsDirectory = true }] }
        };
        Assert.Throws<InvalidDataException>(() => PreOobePlanValidator.ValidatePlan(plan));
    }

    [Fact]
    public void RecoveryBinding_AcceptsStagedSequenceAndIndependentPayloads()
    {
        using var f = new DomainFixture();
        var plan = f.Plan with
        {
            Actions = [new() { Id = "driver", BuiltInKind = PreOobeBuiltInKind.Driver },
                new() { Id = "network", BuiltInKind = PreOobeBuiltInKind.Network }, .. f.Plan.Actions,
                new() { Id = "custom", CustomAction = new() { Kind = Foundry.Core.Models.Configuration.PreOobeActionKind.Command, Process = new() } },
                new() { Id = "cleanup", BuiltInKind = PreOobeBuiltInKind.Cleanup }],
            OwnedPayloads = [.. f.Plan.OwnedPayloads,
                new() { RelativePath = "Payloads/Network/profile.xml", IsSensitive = true, ConsumerActionIds = ["network"] },
                new() { RelativePath = "Payloads/Drivers", IsDirectory = true, ConsumerActionIds = ["driver"] },
                new() { RelativePath = "Work/PreOobe/" + f.Plan.OperationId, IsDirectory = true, ConsumerActionIds = ["custom"] }]
        };
        PreOobePlanValidator.ValidatePlan(plan);
        Assert.Equal(f.Parameters, DomainJoinBinding.Validate(plan).Parameters);
    }

    private sealed class Executor : IPreOobeActionExecutor
    {
        public Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken token) => Task.FromResult(new ActionStepOutcome(true));
    }
}
