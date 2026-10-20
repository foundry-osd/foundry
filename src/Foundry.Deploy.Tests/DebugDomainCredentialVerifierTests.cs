// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.DomainJoin;
using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.Tests;

public sealed class DebugDomainCredentialVerifierTests
{
    [Theory]
    [InlineData(DebugDomainCredentialCheckScenario.Valid, DomainCredentialCheckOutcome.Valid)]
    [InlineData(DebugDomainCredentialCheckScenario.Rejected, DomainCredentialCheckOutcome.InvalidCredentials)]
    [InlineData(DebugDomainCredentialCheckScenario.AccountLocked, DomainCredentialCheckOutcome.AccountLocked)]
    [InlineData(DebugDomainCredentialCheckScenario.PasswordExpired, DomainCredentialCheckOutcome.PasswordExpired)]
    [InlineData(DebugDomainCredentialCheckScenario.AccountDisabled, DomainCredentialCheckOutcome.AccountDisabled)]
    [InlineData(DebugDomainCredentialCheckScenario.DomainUnreachable, DomainCredentialCheckOutcome.ControllerUnavailable)]
    [InlineData(DebugDomainCredentialCheckScenario.Inconclusive, DomainCredentialCheckOutcome.Inconclusive)]
    public async Task EachScenarioAnswersWithItsOutcome(DebugDomainCredentialCheckScenario scenario, DomainCredentialCheckOutcome expected)
    {
        using var verifier = new DebugDomainCredentialVerifier(scenario);

        DomainCredentialCheckResult result = await verifier.VerifyAsync(new("corp.contoso.com", "CORP\\debug"), default, CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task CheckingScenarioStaysPendingUntilTheScenarioIsReleased()
    {
        var verifier = new DebugDomainCredentialVerifier(DebugDomainCredentialCheckScenario.Checking);

        Task<DomainCredentialCheckResult> pending = verifier.VerifyAsync(new("corp.contoso.com", "CORP\\debug"), default, CancellationToken.None);
        Assert.False(pending.IsCompleted);

        verifier.Dispose();

        Assert.Equal(DomainCredentialCheckOutcome.Inconclusive, (await pending).Outcome);
    }
}
