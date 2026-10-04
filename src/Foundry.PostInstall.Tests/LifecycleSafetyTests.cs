// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;

namespace Foundry.PostInstall.Tests;

public sealed class LifecycleSafetyTests
{
    [Fact]
    public async Task LockedUnrelatedSensitivePayload_StillStopsContinuation()
    {
        using var fixture = new Fixture(sensitive: true);
        using var locked = new FileStream(fixture.Payload, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal(3, await fixture.Run("boot"));
        Assert.Equal("Failed", fixture.Journal.Read().Status);
        Assert.Equal("CleanupPending", fixture.Journal.Read().PayloadDispositions["Payloads/secret.bin"]);
    }

    [Fact]
    public async Task UncommittedStagingJournal_CannotStartThroughExistingManualHook()
    {
        using var fixture = new Fixture();
        var state = fixture.Journal.Read();
        state.Status = "Staging";
        fixture.Journal.Write(state);
        Assert.Equal(3, await fixture.Run("boot"));
        Assert.Equal(0, fixture.Calls);
        Assert.True(File.Exists(fixture.Payload));
    }

    [Fact]
    public async Task MaximumCustomActions_LeaveRoomForBuiltInsAndCleanup()
    {
        using var fixture = new Fixture();
        fixture.Plan = fixture.Plan with
        {
            Actions = new[] { new PreOobeExecutionAction { Id = "driver", BuiltInKind = PreOobeBuiltInKind.Driver } }
                .Concat(Enumerable.Range(0, 1000).Select(index => new PreOobeExecutionAction
                {
                    Id = "custom" + index,
                    CustomAction = new() { Kind = PreOobeActionKind.Command, Process = new() }
                }))
                .Append(new() { Id = "cleanup", BuiltInKind = PreOobeBuiltInKind.Cleanup }).ToArray(),
            OwnedPayloads = []
        };
        Assert.Equal(0, await fixture.Run("boot"));
        Assert.Equal(1002, fixture.Calls);
    }

    [Fact]
    public async Task CleanupRejectsAmbiguousOwnedPath_AndPersistsPendingWarning()
    {
        using var fixture = new Fixture();
        fixture.Plan = fixture.Plan with
        {
            OwnedPayloads = [new() { RelativePath = "Payloads/unsafe.", IsDirectory = true, ConsumerActionIds = ["action"] }]
        };
        Assert.Equal(0, await fixture.Run("boot"));
        Assert.Equal("CompletedWithErrors", fixture.Journal.Read().Status);
        Assert.Equal("CleanupPending", fixture.Journal.Read().PayloadDispositions["Payloads/unsafe."]);
    }

    [Fact]
    public async Task MutablePackage_SurvivesTwoCheckpointsWithoutRecheckingOriginalManifest()
    {
        using var fixture = new Fixture();
        string packageRoot = Path.Combine(fixture.Root, "Payloads", "package");
        Directory.CreateDirectory(packageRoot);
        string content = Path.Combine(packageRoot, "input.txt");
        File.WriteAllText(content, "original");
        var manifest = new PreOobePackageManifest
        {
            Files = [new() { RelativePath = "input.txt", Length = new FileInfo(content).Length,
                Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(content))) }]
        };
        fixture.Plan = fixture.Plan with
        {
            Packages = [new() { ContentHash = "hash", RelativePath = "Payloads/package", Manifest = manifest }],
            Actions = [fixture.Plan.Actions[0], Restart("restart1"), Command("second"), Restart("restart2"), Command("last")],
            OwnedPayloads = [new() { RelativePath = "Payloads/package", IsDirectory = true, ConsumerActionIds = ["action", "second", "last"] }]
        };
        fixture.Handler = (action, _) =>
        {
            if (action.Id == "action") { File.WriteAllText(content, "modified"); File.WriteAllText(Path.Combine(packageRoot, "generated.bin"), "generated"); }
            else Assert.Equal("modified", File.ReadAllText(content));
            return new(true);
        };
        Assert.Equal(2, await fixture.Run("a"));
        Assert.Equal(2, await fixture.Run("b"));
        Assert.Equal(0, await fixture.Run("c"));
        Assert.Equal(3, fixture.Calls);
        Assert.False(Directory.Exists(packageRoot));

        static PreOobeExecutionAction Restart(string id) => new() { Id = id, CustomAction = new() { Kind = PreOobeActionKind.Restart } };
        static PreOobeExecutionAction Command(string id) => new() { Id = id, CustomAction = new() { Kind = PreOobeActionKind.Command, Process = new() } };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateSubsteps_PreserveActionStartThroughCompletionAndRestart(bool restart)
    {
        using var fixture = new Fixture();
        fixture.Plan = fixture.Plan with
        {
            Actions = [new() { Id = "action", BuiltInKind = PreOobeBuiltInKind.Appx }]
        };
        var calls = new List<int>();
        var starts = new List<DateTimeOffset?>();
        fixture.Handler = (_, substep) =>
        {
            calls.Add(substep);
            starts.Add(fixture.Journal.Read().Actions["action"].StartedAtUtc);
            return substep switch
            {
                0 => new(true, RestartRequested: restart, NextSubstep: 1),
                1 => new(true, NextSubstep: 2),
                _ => new(true)
            };
        };
        Assert.Equal(restart ? 2 : 0, await fixture.Run("a"));
        if (restart)
        {
            Assert.Equal(1, fixture.Journal.Read().Substep);
            Assert.Equal(0, await fixture.Run("b"));
        }
        Assert.Equal([0, 1, 2], calls);
        Assert.NotNull(starts[0]);
        Assert.All(starts, started => Assert.Equal(starts[0], started));
        var completed = fixture.Journal.Read().Actions["action"];
        Assert.Equal(starts[0], completed.StartedAtUtc);
        Assert.True(completed.CompletedAtUtc >= starts[0]);
    }

    [Fact]
    public async Task UncertainConsumer_KeepsPayloadUntilDifferentBootWithoutReplay()
    {
        using var fixture = new Fixture(sensitive: true);
        fixture.Handler = (_, _) => new(false, FailureCode: "timeout", TerminationUncertain: true);
        Assert.Equal(3, await fixture.Run("a"));
        Assert.True(File.Exists(fixture.Payload));
        Assert.Equal("CleanupPending", fixture.Journal.Read().PayloadDispositions["Payloads/secret.bin"]);
        Assert.Equal(3, await fixture.Run("a"));
        Assert.Equal(3, await fixture.Run("b"));
        Assert.False(File.Exists(fixture.Payload));
        Assert.Equal(1, fixture.Calls);
    }

    [Fact]
    public async Task UncertainAction_DoesNotRetainIndependentUnconsumedSecret()
    {
        using var fixture = new Fixture(sensitive: true);
        fixture.Plan = fixture.Plan with
        {
            Actions = [fixture.Plan.Actions[0], new() { Id = "later", CustomAction = new() { Kind = PreOobeActionKind.Command, Process = new() } }],
            OwnedPayloads = [new() { RelativePath = "Payloads/secret.bin", IsSensitive = true, ConsumerActionIds = ["later"] }]
        };
        fixture.Handler = (_, _) => new(false, TerminationUncertain: true);
        Assert.Equal(3, await fixture.Run("a"));
        Assert.False(File.Exists(fixture.Payload));
        Assert.Equal(1, fixture.Calls);
    }

    [Fact]
    public async Task SensitiveDisposalIntent_IsCommittedBeforeConsumerStarts()
    {
        using var fixture = new Fixture(sensitive: true);
        fixture.Handler = (_, _) =>
        {
            Assert.Equal("DisposalRequired", fixture.Journal.Read().PayloadDispositions["Payloads/secret.bin"]);
            return new(true);
        };
        Assert.Equal(0, await fixture.Run("a"));
        Assert.False(File.Exists(fixture.Payload));
    }

    [Fact]
    public async Task CompletingJournal_OnlyReconcilesCleanup()
    {
        using var fixture = new Fixture();
        JournalState state = fixture.Journal.Read();
        state.Status = "Completing";
        state.CompletionStatus = "Succeeded";
        state.Cursor = 1;
        state.Actions["action"] = new() { Status = "Succeeded" };
        fixture.Journal.Write(state);
        Assert.Equal(0, await fixture.Run("a"));
        Assert.Equal(0, fixture.Calls);
        Assert.False(File.Exists(fixture.Payload));
    }

    [Fact]
    public async Task FailedCheckpointPublication_NeverReturnsRestartOrReplaysAction()
    {
        using var fixture = new Fixture(failCheckpoint: true);
        fixture.Handler = (_, _) => new(true, 3010, RestartRequested: true);
        Assert.Equal(3, await fixture.Run("a"));
        Assert.Equal(3, await fixture.Run("b"));
        Assert.Equal(1, fixture.Calls);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("nested/file:stream")]
    [InlineData("nested//file")]
    public void OwnedPaths_RejectAmbiguousNames(string relative) =>
        Assert.Throws<InvalidDataException>(() => OwnedPaths.Resolve(Path.GetTempPath(), relative));

    private sealed class Fixture : IDisposable, IPreOobeActionExecutor
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Foundry.PostInstall.Tests", Guid.NewGuid().ToString("N"));
        public string Payload => Path.Combine(Root, "Payloads", "secret.bin");
        public PreOobeExecutionPlan Plan { get; set; }
        public ExecutionJournal Journal { get; }
        public Func<PreOobeExecutionAction, int, ActionStepOutcome> Handler { get; set; } = (_, _) => new(true);
        public int Calls { get; private set; }

        public Fixture(bool sensitive = false, bool failCheckpoint = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Payload)!);
            File.WriteAllText(Payload, "fixture");
            Plan = new()
            {
                OperationId = Guid.NewGuid().ToString("N"),
                AttemptId = "attempt",
                Actions = [new() { Id = "action", CustomAction = new() { Id = "action", Kind = PreOobeActionKind.Command, Process = new() } }],
                OwnedPayloads = [new() { RelativePath = "Payloads/secret.bin", IsSensitive = sensitive, ConsumerActionIds = ["action"] }]
            };
            Journal = failCheckpoint ? new FailingJournal(Root) : new ExecutionJournal(Root);
            Journal.Seed(Plan, "hash");
        }

        public async Task<int> Run(string boot) => (await new PreOobeOrchestrator(Root, "hash", Journal, this, () => boot)
            .RunAsync(Plan, TestContext.Current.CancellationToken)).ExitCode;

        public Task<ActionStepOutcome> ExecuteAsync(PreOobeExecutionAction action, int substep, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Handler(action, substep)); }

        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class FailingJournal(string root) : ExecutionJournal(root)
    {
        public override void Write(JournalState state)
        {
            if (state.Status == "AwaitingRestart") throw new IOException("Injected checkpoint failure.");
            base.Write(state);
        }
    }
}
