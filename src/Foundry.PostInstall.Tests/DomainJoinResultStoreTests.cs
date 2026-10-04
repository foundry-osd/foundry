// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.PreOobe;
using Foundry.PostInstall.Execution;
namespace Foundry.PostInstall.Tests;

public sealed class DomainJoinResultStoreTests
{
    [Theory]
    [InlineData("phase")]
    [InlineData("generation")]
    [InlineData("originatingBootId")]
    [InlineData("join")]
    [InlineData("placement")]
    [InlineData("restartRequired")]
    [InlineData("join.state")]
    public async Task MissingAuthoritativeReceiptField_NeverAuthorizesExecution(string field)
    {
        using var f = new DomainFixture();
        string path = Path.Combine(f.Root, "State", "PreOobe", "domain-join-phase.json");
        var value = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (field == "join.state") value["join"]!.AsObject().Remove("state"); else value.Remove(field);
        File.WriteAllText(path, value.ToJsonString());
        await Assert.ThrowsAsync<JsonException>(f.Run);
        Assert.Equal(0, f.Directory.Prepares); Assert.Equal(0, f.Native.Joins);
    }
    [Fact]
    public void PreparedSeed_BindsOriginOnceAndRejectsStartedReplay()
    {
        using var f = new DomainFixture();
        var store = new DomainJoinPhaseStore(f.Root, f.Plan, f.Hash);
        Assert.Null(store.Read().OriginatingBootId);
        var bound = store.BindOrigin("installed-boot");
        Assert.Equal(1, bound.Generation);
        Assert.Throws<InvalidDataException>(() => store.BindOrigin("other-boot"));
        store.Write(bound with { Generation = 2, Phase = DomainJoinReceiptPhase.JoinStarted });
        Assert.Throws<InvalidDataException>(() => store.Write(bound with { Generation = 3 }));
    }
    [Fact]
    public void Receipt_RejectsWrongIdentityAndUnassignedMutation()
    {
        using var f = new DomainFixture();
        var store = new DomainJoinPhaseStore(f.Root, f.Plan, f.Hash);
        var seed = store.Read();
        Assert.Throws<InvalidDataException>(() => store.Write(seed with { Generation = 1, OperationId = Guid.NewGuid().ToString("N") }));
        Assert.Throws<InvalidDataException>(() => store.Write(seed with { Generation = 1, Phase = DomainJoinReceiptPhase.JoinStarted }));
    }
    [Fact]
    public void Report_RejectsForeignIdentityAndSuccessfulJoinWithoutRestart()
    {
        using var f = new DomainFixture();
        var store = new DomainJoinResultStore(f.Root, f.Plan, f.Hash); store.Seed();
        var seed = store.Read();
        Assert.Throws<InvalidDataException>(() => store.Write(seed with { PlanHash = new string('b', 64) }));
        Assert.Throws<InvalidDataException>(() => store.Write(seed with { OriginatingBootId = "installed-boot", Join = new() { State = DomainJoinPhaseState.Succeeded } }));
    }
    [Fact]
    public void Report_RejectsConflatedErrorFamilies()
    {
        using var f = new DomainFixture(); var store = new DomainJoinResultStore(f.Root, f.Plan, f.Hash); store.Seed();
        Assert.Throws<InvalidDataException>(() => store.Write(store.Read() with
        {
            OriginatingBootId = "installed-boot",
            Join = new() { State = DomainJoinPhaseState.Failed, FailureCode = DomainJoinFailureCode.DomainUnavailable, LdapErrorCode = 49, DirectoryResultCode = 50 }
        }));
    }
    [Fact]
    public void Report_AcceptsRecordedOriginAfterRebootButNeverChangesIt()
    {
        using var f = new DomainFixture();
        var store = new DomainJoinResultStore(f.Root, f.Plan, f.Hash); store.Seed();
        store.Write(store.Read() with { OriginatingBootId = "installed-boot", Join = new() { State = DomainJoinPhaseState.Succeeded }, Restart = DomainJoinRestartState.Required });
        var resumed = new DomainJoinResultStore(f.Root, f.Plan, f.Hash); Assert.Equal("installed-boot", resumed.Read().OriginatingBootId);
        Assert.Throws<InvalidDataException>(() => resumed.Write(resumed.Read() with { OriginatingBootId = "next-boot" }));
    }
}
