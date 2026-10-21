// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.DomainJoin;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DomainJoin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class ZeroTouchDomainCredentialCheckTests
{
    [Theory]
    [InlineData(DomainCredentialCheckOutcome.InvalidCredentials, "DomainJoin.ZeroTouchCredentialsRejected")]
    [InlineData(DomainCredentialCheckOutcome.AccountLocked, "DomainJoin.ZeroTouchAccountLocked")]
    [InlineData(DomainCredentialCheckOutcome.PasswordExpired, "DomainJoin.ZeroTouchPasswordExpired")]
    [InlineData(DomainCredentialCheckOutcome.AccountDisabled, "DomainJoin.ZeroTouchAccountDisabled")]
    public async Task RejectionBlocksTheStartAfterOneCheckOfTheMediaAccount(DomainCredentialCheckOutcome outcome, string resourceKey)
    {
        var verifier = new RecordingVerifier(outcome);
        using DomainJoinPreparedInput input = MediaInput();

        ZeroTouchDomainCredentialCheckDecision decision = await CreateCheck(verifier).CheckAsync(ReadyContext(DomainJoinMode.Automatic), input, CancellationToken.None);

        Assert.False(decision.CanStart);
        Assert.Equal(resourceKey, decision.FailureResourceKey);
        Assert.Equal(1, verifier.Calls);
        Assert.Equal("corp.test", verifier.LastContext?.DomainName);
        Assert.Equal("CORP\\join", verifier.LastContext?.AccountName);
        Assert.Equal("media secret", verifier.LastPassword);
    }

    [Theory]
    [InlineData(DomainCredentialCheckOutcome.Valid)]
    [InlineData(DomainCredentialCheckOutcome.ControllerUnavailable)]
    [InlineData(DomainCredentialCheckOutcome.Inconclusive)]
    public async Task ValidUnreachableAndInconclusiveLetTheDeploymentStart(DomainCredentialCheckOutcome outcome)
    {
        var verifier = new RecordingVerifier(outcome);
        using DomainJoinPreparedInput input = MediaInput();

        ZeroTouchDomainCredentialCheckDecision decision = await CreateCheck(verifier).CheckAsync(ReadyContext(DomainJoinMode.Automatic), input, CancellationToken.None);

        Assert.True(decision.CanStart);
        Assert.Null(decision.FailureResourceKey);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task UnexpectedVerifierFailureLetsTheDeploymentStart()
    {
        var verifier = new RecordingVerifier(new DllNotFoundException());
        using DomainJoinPreparedInput input = MediaInput();

        ZeroTouchDomainCredentialCheckDecision decision = await CreateCheck(verifier).CheckAsync(ReadyContext(DomainJoinMode.Automatic), input, CancellationToken.None);

        Assert.True(decision.CanStart);
    }

    [Theory]
    [InlineData("interactive")]
    [InlineData("declined")]
    [InlineData("unsupportedEdition")]
    [InlineData("dryRun")]
    [InlineData("noJoin")]
    public async Task OtherDeploymentsNeverContactTheDomain(string kind)
    {
        var verifier = new RecordingVerifier(DomainCredentialCheckOutcome.InvalidCredentials);
        using DomainJoinPreparedInput input = MediaInput();
        DeploymentContext context = kind switch
        {
            "interactive" => ReadyContext(DomainJoinMode.Interactive),
            "declined" => BaseContext() with { DomainJoinRequest = new(DomainJoinDeploymentDisposition.Declined, DomainJoinMode.Automatic) },
            "unsupportedEdition" => BaseContext() with { DomainJoinRequest = new(DomainJoinDeploymentDisposition.UnsupportedEdition, DomainJoinMode.Automatic) },
            "dryRun" => BaseContext() with { IsDryRun = true, DomainJoinRequest = new(DomainJoinDeploymentDisposition.DryRun, DomainJoinMode.Automatic) },
            _ => BaseContext()
        };

        ZeroTouchDomainCredentialCheckDecision decision = await CreateCheck(verifier).CheckAsync(context, kind == "interactive" ? input : null, CancellationToken.None);

        Assert.True(decision.CanStart);
        Assert.Equal(0, verifier.Calls);
    }

    private static ZeroTouchDomainCredentialCheck CreateCheck(IDomainCredentialVerifier verifier) =>
        new(verifier, NullLogger<ZeroTouchDomainCredentialCheck>.Instance);

    private static DomainJoinPreparedInput MediaInput() =>
        new(new("corp.test", "CORP\\join"), "LAB-01", null, "media secret".AsSpan());

    private static DeploymentContext ReadyContext(DomainJoinMode mode) => BaseContext() with
    {
        DomainJoinRequest = new(DomainJoinDeploymentDisposition.Ready, mode),
        DomainJoinIntent = new("corp.test", "LAB-01", null)
    };

    private static DeploymentContext BaseContext() => new()
    {
        Mode = DeploymentMode.Iso,
        CacheRootPath = "X:\\Cache",
        TargetDiskNumber = 1,
        TargetComputerName = "LAB-01",
        OperatingSystem = new OperatingSystemCatalogItem(),
        DriverPackSelectionKind = DriverPackSelectionKind.None
    };

    private sealed class RecordingVerifier : IDomainCredentialVerifier
    {
        private readonly Task<DomainCredentialCheckResult> result;

        public RecordingVerifier(DomainCredentialCheckOutcome outcome) => result = Task.FromResult(new DomainCredentialCheckResult(outcome));

        public RecordingVerifier(Exception failure) => result = Task.FromException<DomainCredentialCheckResult>(failure);

        public int Calls { get; private set; }

        public DomainJoinCredentialContext? LastContext { get; private set; }

        public string? LastPassword { get; private set; }

        public Task<DomainCredentialCheckResult> VerifyAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password, CancellationToken token)
        {
            Calls++;
            LastContext = context;
            LastPassword = new string(password.Span);
            return result;
        }
    }
}
