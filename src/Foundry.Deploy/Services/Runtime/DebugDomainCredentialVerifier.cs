// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.DomainJoin;
using Foundry.Deploy.Services.DomainJoin;

namespace Foundry.Deploy.Services.Runtime;

/// <summary>Selects the answer the Domain Join credential check gives in Foundry.Deploy debug safe mode.</summary>
public enum DebugDomainCredentialCheckScenario
{
    /// <summary>The check does not run, as on media without a reachable domain in debug safe mode.</summary>
    None,

    /// <summary>The check never answers, so the progress line stays visible.</summary>
    Checking,

    /// <summary>The domain accepts the account.</summary>
    Valid,

    /// <summary>The domain rejects the account name or the password.</summary>
    Rejected,

    /// <summary>The account is locked out.</summary>
    AccountLocked,

    /// <summary>The password has expired or must be changed.</summary>
    PasswordExpired,

    /// <summary>The account is disabled or expired.</summary>
    AccountDisabled,

    /// <summary>No domain controller can be reached.</summary>
    DomainUnreachable,

    /// <summary>The domain answers without saying whether the account is valid.</summary>
    Inconclusive
}

/// <summary>
/// Answers the credential check with a scripted outcome and never contacts a domain. It is used only in debug safe mode;
/// disposing it releases a pending <see cref="DebugDomainCredentialCheckScenario.Checking"/> answer as inconclusive, so a
/// waiting wizard command can end when another scenario is chosen.
/// </summary>
public sealed class DebugDomainCredentialVerifier(DebugDomainCredentialCheckScenario scenario) : IDomainCredentialVerifier, IZeroTouchMediaCredentialCheck, IDisposable
{
    private readonly TaskCompletionSource<DomainCredentialCheckResult> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public Task<DomainCredentialCheckResult> VerifyAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password, CancellationToken token) =>
        scenario switch
        {
            DebugDomainCredentialCheckScenario.Checking => pending.Task,
            DebugDomainCredentialCheckScenario.Valid => Answer(DomainCredentialCheckOutcome.Valid),
            DebugDomainCredentialCheckScenario.Rejected => Answer(DomainCredentialCheckOutcome.InvalidCredentials),
            DebugDomainCredentialCheckScenario.AccountLocked => Answer(DomainCredentialCheckOutcome.AccountLocked),
            DebugDomainCredentialCheckScenario.PasswordExpired => Answer(DomainCredentialCheckOutcome.PasswordExpired),
            DebugDomainCredentialCheckScenario.AccountDisabled => Answer(DomainCredentialCheckOutcome.AccountDisabled),
            DebugDomainCredentialCheckScenario.DomainUnreachable => Answer(DomainCredentialCheckOutcome.ControllerUnavailable),
            _ => Answer(DomainCredentialCheckOutcome.Inconclusive)
        };

    /// <summary>Answers for the selected debug domain without decrypting anything: debug media carries no credentials.</summary>
    public async Task<DomainCredentialCheckResult?> VerifyMediaCredentialsAsync(DeployDomainJoinSettings settings, string? selectedDomainId,
        IDomainCredentialVerifier verifier, CancellationToken token)
    {
        DeployDomainJoinDomainSettings? domain = DomainJoinPreparationService.ResolveDomain(settings, selectedDomainId);
        return domain is null ? null : await verifier.VerifyAsync(new(domain.DomainName, domain.AccountName ?? string.Empty), ReadOnlyMemory<char>.Empty, token);
    }

    public void Dispose() => pending.TrySetResult(new(DomainCredentialCheckOutcome.Inconclusive));

    private static Task<DomainCredentialCheckResult> Answer(DomainCredentialCheckOutcome outcome) => Task.FromResult(new DomainCredentialCheckResult(outcome));
}
