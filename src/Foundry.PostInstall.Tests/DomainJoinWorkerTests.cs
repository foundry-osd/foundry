// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;
using Foundry.PostInstall.Execution;
using Foundry.PostInstall.Windows;

namespace Foundry.PostInstall.Tests;

public sealed class DomainJoinWorkerTests
{
    [Fact]
    public async Task NewVisibleAccount_UsesResolvedCreationOuWithoutMoving()
    {
        using var f = new DomainFixture();
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Succeeded, result.Join.State);
        Assert.Equal(f.Directory.Target.DistinguishedName, f.Native.CreationOu);
        Assert.Equal("example.test\\dc.example.test", f.Native.DomainAndDc);
        Assert.Equal(0, f.Directory.Moves);
        Assert.True(result.RestartRequired);
    }
    [Fact]
    public async Task ExistingAccount_MovesSameGuidAfterJoin()
    {
        using var f = new DomainFixture();
        f.Directory.Existing = f.Directory.Computer;
        var result = await f.Run();
        Assert.Null(f.Native.CreationOu);
        Assert.Equal(1, f.Directory.Moves);
        Assert.Equal(f.Directory.Computer.Guid, result.ComputerObjectGuid);
        Assert.Equal(DomainJoinPhaseState.Succeeded, result.Placement.State);
    }
    [Theory]
    [InlineData(@"CN=Original\, RDN")]
    [InlineData(@"CN=Original\2C RDN")]
    [InlineData(@"CN=Original\, RDN+UID=Serial\+42")]
    public void MoveRequest_PreservesSourceDestinationAndCompleteEscapedRdn(string originalRdn)
    {
        string sourceDn = originalRdn + ",OU=Old,DC=example,DC=test";
        const string destinationDn = "OU=Workstations,DC=example,DC=test";

        var request = DomainComputerAccountDirectory.CreateMoveRequest(sourceDn, destinationDn);

        Assert.Equal(sourceDn, request.DistinguishedName);
        Assert.Equal(destinationDn, request.NewParentDistinguishedName);
        Assert.Equal(originalRdn, request.NewName);
        Assert.True(request.DeleteOldRdn);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameParentOrNoDestination_DoesNotMove(bool sameParent)
    {
        using var f = new DomainFixture(sameParent);
        f.Directory.Existing = f.Directory.Computer with { ParentGuid = f.Directory.Target.Guid };
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Succeeded, result.Join.State);
        Assert.Equal(0, f.Directory.Moves);
    }
    [Fact]
    public async Task IncompleteReadiness_NeverJoins()
    {
        using var f = new DomainFixture();
        f.Directory.ReadinessFailure = true;
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Failed, result.Join.State);
        Assert.Equal(0, f.Native.Joins);
        Assert.Equal(1, f.Directory.Prepares);
    }
    [Fact]
    public async Task MissingTargetOu_JoinsInTheDefaultLocationAndReportsPlacementFailure()
    {
        using var f = new DomainFixture();
        f.Directory.MissingTargetOu = true;
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Succeeded, result.Join.State);
        Assert.True(result.RestartRequired);
        Assert.Null(f.Native.CreationOu);
        Assert.Equal(DomainJoinPhaseState.Failed, result.Placement.State);
        Assert.Equal(DomainJoinFailureCode.OrganizationalUnitNotFound, result.Placement.FailureCode);
        Assert.Equal(0, f.Directory.Moves);
    }

    [Fact]
    public async Task UnreachableDirectory_IsRetriedUntilReadyThenJoins()
    {
        using var f = new DomainFixture();
        f.Directory.TransientFailures = 2;
        int waits = 0;
        f.Delay = (_, _) => { waits++; return Task.CompletedTask; };
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Succeeded, result.Join.State);
        Assert.Equal(3, f.Directory.Prepares);
        Assert.Equal(2, waits);
        Assert.Equal(1, f.Native.Joins);
    }
    [Fact]
    public async Task UnreachableDirectoryBeyondReadinessBudget_ReportsTimeoutWithoutJoining()
    {
        using var f = new DomainFixture();
        f.Directory.TransientFailures = int.MaxValue;
        int waits = 0;
        f.Delay = (_, token) => ++waits < 3 ? Task.CompletedTask : Task.FromCanceled(new CancellationToken(true));
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Failed, result.Join.State);
        Assert.Equal(DomainJoinFailureCode.ReadinessTimeout, result.Join.FailureCode);
        Assert.Equal(1355, result.Join.NativeErrorCode);
        Assert.Equal(0, f.Native.Joins);
    }
    [Fact]
    public async Task FailedJoin_NeverMoves()
    {
        using var f = new DomainFixture();
        f.Directory.Existing = f.Directory.Computer;
        f.Native.JoinCode = 5;
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Failed, result.Join.State);
        Assert.Equal(0, f.Directory.Moves);
        Assert.False(result.RestartRequired);
    }
    [Fact]
    public async Task ChangedActiveName_IsPreparedBeforeJoinWithPendingFlag()
    {
        using var f = new DomainFixture();
        f.Native.Name = "OLD";
        await f.Run();
        Assert.Equal(["set", "join"], f.Native.Calls);
        Assert.True(f.Native.PendingName);
    }
    [Fact]
    public async Task LostNativeResponse_IsUnknownAndNeverReplayed()
    {
        using var f = new DomainFixture();
        f.Native.ThrowOnJoin = true;
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Unknown, result.Join.State);
        await Assert.ThrowsAsync<InvalidDataException>(f.Run);
        Assert.Equal(1, f.Native.Joins);
    }
    [Fact]
    public async Task AcknowledgedMoveWithDeniedReadback_IsUnverified()
    {
        using var f = new DomainFixture();
        f.Directory.Existing = f.Directory.Computer;
        f.Directory.DenyReadback = true;
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Unverified, result.Placement.State);
        Assert.True(result.RestartRequired);
    }
    [Fact]
    public async Task LostMoveResponse_RemainsUnknownEvenWhenReadbackShowsDestination()
    {
        using var f = new DomainFixture();
        f.Directory.Existing = f.Directory.Computer;
        f.Directory.LoseMoveResponse = true;
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Unknown, result.Placement.State);
        Assert.Equal(1, f.Directory.Moves);
    }
    [Fact]
    public async Task HiddenExistingAccount_IsNeverRelocatedWithoutPreJoinGuid()
    {
        using var f = new DomainFixture();
        f.Directory.NewParent = Guid.NewGuid();
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Unverified, result.Placement.State);
        Assert.Equal(0, f.Directory.Moves);
    }
    [Fact]
    public async Task StaleIdentity_PreventsMove()
    {
        using var f = new DomainFixture();
        f.Directory.Existing = f.Directory.Computer;
        f.Directory.StaleGuid = true;
        var result = await f.Run();
        Assert.Equal(0, f.Directory.Moves);
        Assert.Equal(DomainJoinPhaseState.Failed, result.Placement.State);
    }
    [Fact]
    public async Task MutationCalls_ObserveDurableStartedAndJoinReturnReceipts()
    {
        using var f = new DomainFixture(); f.Directory.Existing = f.Directory.Computer;
        DomainJoinPhaseReceipt? joinReceipt = null;
        DomainJoinPhaseReceipt? moveReceipt = null;
        f.Native.BeforeJoin = () => joinReceipt = new DomainJoinPhaseStore(f.Root, f.Plan, f.Hash).Read();
        f.Directory.BeforeMove = () => moveReceipt = new DomainJoinPhaseStore(f.Root, f.Plan, f.Hash).Read();
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Succeeded, result.Placement.State);
        Assert.NotNull(joinReceipt); Assert.Equal(DomainJoinReceiptPhase.JoinStarted, joinReceipt.Phase);
        Assert.NotNull(moveReceipt); Assert.Equal(DomainJoinReceiptPhase.PlacementStarted, moveReceipt.Phase);
        Assert.Equal(DomainJoinPhaseState.Succeeded, moveReceipt.Join.State); Assert.True(moveReceipt.RestartRequired);
    }
    [Fact]
    public async Task ReceiptWriteFailure_PreventsNativeMutation()
    {
        using var f = new DomainFixture();
        f.Directory.AfterPrepare = () => File.Delete(Path.Combine(f.Root, "State", "PreOobe", "domain-join-phase.json"));
        await Assert.ThrowsAsync<FileNotFoundException>(f.Run);
        Assert.Equal(0, f.Native.Joins); Assert.Empty(f.Native.Calls);
    }
    [Fact]
    public async Task CredentialDomainMismatch_PreventsAuthentication()
    {
        using var f = new DomainFixture();
        byte[] bytes = DomainJoinCredentialPayloadCodec.Encode(new("foreign.test", "EXAMPLE\\joiner"), "secret".AsSpan());
        File.WriteAllBytes(OwnedPaths.Resolve(f.Root, f.Parameters.CredentialPayloadPath), bytes); CryptographicOperations.ZeroMemory(bytes);
        var result = await f.Run();
        Assert.Equal(DomainJoinFailureCode.ContextMismatch, result.Join.FailureCode);
        Assert.Equal(0, f.Directory.Prepares); Assert.Equal(0, f.Native.Joins);
    }
    [Fact]
    public async Task ForeignDestination_PreventsJoin()
    {
        using var f = new DomainFixture(); f.Directory.Target = f.Directory.Target with { DistinguishedName = "OU=Target,DC=foreign,DC=test" };
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Failed, result.Join.State); Assert.Equal(0, f.Native.Joins);
    }
    [Fact]
    public async Task TargetCollision_IsReportedWithoutRetryingTheMove()
    {
        using var f = new DomainFixture(); f.Directory.Existing = f.Directory.Computer; f.Directory.RejectMove = true;
        var result = await f.Run();
        Assert.Equal(DomainJoinPhaseState.Failed, result.Placement.State); Assert.Equal(1, f.Directory.Moves);
        Assert.Equal(68, result.Placement.DirectoryResultCode); Assert.Null(result.Placement.LdapErrorCode);
    }
    [Fact]
    public async Task Worker_ReleasesDecodedPasswordWhenDirectoryReadinessFails()
    {
        using var f = new DomainFixture(); f.Directory.ReadinessFailure = true;
        await f.Run();
        Assert.Equal(6, f.Directory.ObservedPassword.Length);
        Assert.All(f.Directory.ObservedPassword.ToArray(), character => Assert.Equal('\0', character));
    }

}

internal sealed class DomainFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "foundry-domain-" + Guid.NewGuid().ToString("N"));
    public PreOobeExecutionPlan Plan { get; }
    public DomainJoinActionParameters Parameters { get; }
    public string Hash { get; }
    public FakeNative Native { get; } = new();
    public FakeDirectory Directory { get; } = new();
    public DomainFixture(bool target = true, Func<PreOobeExecutionPlan, PreOobeExecutionPlan>? configure = null)
    {
        string operation = Guid.NewGuid().ToString("N");
        Parameters = new("example.test", "PC-01", target ? Directory.Target.DistinguishedName : null, $"Payloads/DomainJoin/{operation}/credentials.bin");
        Plan = new()
        {
            OperationId = operation,
            AttemptId = Guid.NewGuid().ToString("N"),
            Actions = [new() { Id = "join", BuiltInKind = PreOobeBuiltInKind.DomainJoinAndPlacement, Parameters = JsonSerializer.SerializeToElement(Parameters, ExecutionJournal.JsonOptions) },
                new() { Id = "verify", BuiltInKind = PreOobeBuiltInKind.VerifyDomainMembership, Parameters = JsonSerializer.SerializeToElement(new DomainMembershipVerificationParameters("example.test", "PC-01", "join"), ExecutionJournal.JsonOptions) }],
            OwnedPayloads = [new() { RelativePath = Parameters.CredentialPayloadPath, IsSensitive = true, ConsumerActionIds = ["join"] }]
        };
        if (configure is not null) Plan = configure(Plan);
        System.IO.Directory.CreateDirectory(Path.Combine(Root, "State", "PreOobe"));
        byte[] planBytes = JsonSerializer.SerializeToUtf8Bytes(Plan, ExecutionJournal.JsonOptions);
        File.WriteAllBytes(Path.Combine(Root, "State", "PreOobe", "plan.json"), planBytes);
        Hash = Convert.ToHexStringLower(SHA256.HashData(planBytes));
        DomainSeeds.WritePhase(Root, Plan, Hash);
        var journal = new ExecutionJournal(Root);
        journal.Seed(Plan, Hash);
        var state = journal.Read(); state.Status = "Running"; state.BootIdentity = "installed-boot";
        state.Actions["join"] = new() { Status = "Running" }; journal.Write(state);
        string credentials = OwnedPaths.Resolve(Root, Parameters.CredentialPayloadPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(credentials)!);
        byte[] bytes = DomainJoinCredentialPayloadCodec.Encode(new("example.test", "EXAMPLE\\joiner"), "secret".AsSpan());
        File.WriteAllBytes(credentials, bytes); CryptographicOperations.ZeroMemory(bytes);
    }
    public Func<TimeSpan, CancellationToken, Task>? Delay;
    public Task<DomainJoinWorkerResult> Run() => new DomainJoinWorker(Root, Plan, Hash, "installed-boot", Native, Directory, Delay).RunAsync(Parameters, CancellationToken.None);
    public void Dispose() => System.IO.Directory.Delete(Root, true);
}

/// <summary>Writes the two state files as Deploy stages them, so tests start from the production seed shape.</summary>
internal static class DomainSeeds
{
    public static void WriteResult(string root, PreOobeExecutionPlan plan, string hash) => DomainStateFile.Write(root,
        OwnedPaths.Resolve(root, "State/PreOobe/domain-join-result.json"), new DomainJoinResultStore(root, plan, hash).CreateSeed(), create: true);

    public static void WritePhase(string root, PreOobeExecutionPlan plan, string hash) => DomainStateFile.Write(root,
        OwnedPaths.Resolve(root, "State/PreOobe/domain-join-phase.json"), new DomainJoinPhaseStore(root, plan, hash).CreateSeed(), create: true);
}

internal sealed class FakeNative : INativeDomainJoin
{
    public string Name = "PC-01";
    public int Joins;
    public int JoinCode;
    public bool ThrowOnJoin;
    public string? CreationOu;
    public string? DomainAndDc;
    public bool PendingName;
    public List<string> Calls = [];
    public Action? BeforeJoin;
    public string GetActiveComputerName() => Name;
    public int SetComputerName(string name) { Calls.Add("set"); return 0; }
    public int Join(string domainAndDc, string? creationOu, DomainJoinCredentialContext context, ReadOnlySpan<char> password, bool usePendingName)
    {
        BeforeJoin?.Invoke(); Joins++; Calls.Add("join"); CreationOu = creationOu; DomainAndDc = domainAndDc; PendingName = usePendingName;
        if (ThrowOnJoin) throw new IOException();
        return JoinCode;
    }
    public DomainMembershipSnapshot GetMembership() => new(3, "example.test", Name);
}
internal sealed class FakeDirectory : IDomainComputerAccountDirectory
{
    public DomainDirectoryObject Target = new(Guid.NewGuid(), "OU=Workstations,DC=example,DC=test");
    public DomainDirectoryObject Computer = new(Guid.NewGuid(), "CN=Original\\, RDN,OU=Old,DC=example,DC=test", Guid.NewGuid());
    public DomainDirectoryObject? Existing;
    public Guid? NewParent;
    public bool ReadinessFailure;
    public bool MissingTargetOu;
    public int TransientFailures;
    public bool DenyReadback;
    public bool LoseMoveResponse;
    public bool StaleGuid;
    public int Moves;
    public int Prepares;
    public bool RejectMove;
    public Action? BeforeMove;
    public Action? AfterPrepare;
    public ReadOnlyMemory<char> ObservedPassword;
    public Task<DomainDirectoryReady> PrepareAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password, string computerName, string? targetOuDn, CancellationToken token)
    {
        Prepares++; ObservedPassword = password; AfterPrepare?.Invoke();
        if (Prepares <= TransientFailures) throw new DomainDirectoryException(nativeError: 1355, transient: true);
        if (ReadinessFailure) throw new DomainDirectoryException(ldapError: 50);
        if (MissingTargetOu && targetOuDn is not null) return Task.FromResult(new DomainDirectoryReady("example.test", "dc.example.test", null, null, DestinationMissing: true));
        return Task.FromResult(new DomainDirectoryReady("example.test", "dc.example.test", targetOuDn is null ? null : Target, Existing));
    }
    public Task<DomainDirectoryObject?> FindComputerAsync(string name, CancellationToken token) => Task.FromResult<DomainDirectoryObject?>(Computer with { ParentGuid = NewParent ?? Target.Guid });
    public Task<DomainDirectoryObject> ReadComputerAsync(Guid guid, CancellationToken token)
    {
        if (Moves > 0 && DenyReadback) throw new DomainDirectoryException(ldapError: 50);
        return Task.FromResult((Existing ?? Computer) with { Guid = StaleGuid ? Guid.NewGuid() : guid, ParentGuid = Moves > 0 ? Target.Guid : (Existing ?? Computer).ParentGuid });
    }
    public Task<DomainDirectoryObject> ReadDestinationAsync(Guid guid, CancellationToken token) => Task.FromResult(Target);
    public Task MoveAsync(DomainDirectoryObject computer, DomainDirectoryObject destination, CancellationToken token)
    {
        BeforeMove?.Invoke(); Moves++;
        if (RejectMove) throw new DomainDirectoryException(directoryResult: 68, rejected: true);
        if (LoseMoveResponse) throw new DomainDirectoryException();
        return Task.CompletedTask;
    }
    public void Dispose() { }
}
