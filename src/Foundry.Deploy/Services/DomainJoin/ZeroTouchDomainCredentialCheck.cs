// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.DomainJoin;
using Foundry.Deploy.Services.Deployment;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>
/// Says whether a prepared deployment may start after the Zero-touch join account was checked.
/// <see cref="FailureResourceKey"/> names the localized launch failure when the domain refused the account.
/// </summary>
public sealed record ZeroTouchDomainCredentialCheckDecision(bool CanStart, string? FailureResourceKey = null)
{
    /// <summary>Gets the decision for a deployment that does not check, or whose check does not block.</summary>
    public static ZeroTouchDomainCredentialCheckDecision Start { get; } = new(true);
}

/// <summary>Checks the join account stored on Zero-touch media before a deployment changes the disk.</summary>
public interface IZeroTouchDomainCredentialCheck
{
    /// <summary>
    /// Checks the prepared account and password with the domain when the prepared join is ready in Zero-touch mode and is not a
    /// dry run. Only a refusal by the domain blocks the start; an unreachable domain or an unclear answer logs a warning.
    /// </summary>
    Task<ZeroTouchDomainCredentialCheckDecision> CheckAsync(DeploymentContext context, DomainJoinPreparedInput? input, CancellationToken token);
}

/// <inheritdoc cref="IZeroTouchDomainCredentialCheck" />
public sealed class ZeroTouchDomainCredentialCheck(IDomainCredentialVerifier verifier, ILogger<ZeroTouchDomainCredentialCheck> logger)
    : IZeroTouchDomainCredentialCheck
{
    /// <inheritdoc />
    public async Task<ZeroTouchDomainCredentialCheckDecision> CheckAsync(DeploymentContext context, DomainJoinPreparedInput? input, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (input is null || context.IsDryRun ||
            context.DomainJoinRequest is not { Disposition: DomainJoinDeploymentDisposition.Ready, Mode: DomainJoinMode.Automatic })
        {
            return ZeroTouchDomainCredentialCheckDecision.Start;
        }

        logger.LogInformation("Zero-touch domain credential check started.");
        DomainCredentialCheckResult result;
        try
        {
            // The prepared input owns the only password copy; the verifier releases its own copies.
            result = await verifier.VerifyAsync(input.CredentialContext, input.Password, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            result = new(DomainCredentialCheckOutcome.Inconclusive);
        }

        logger.Log(
            result.Outcome == DomainCredentialCheckOutcome.Valid ? LogLevel.Information : result.IsRejection ? LogLevel.Error : LogLevel.Warning,
            "Zero-touch domain credential check completed with {Outcome} (LDAP {LdapErrorCode}, data {DirectoryDataCode:x}, native {NativeErrorCode}).",
            result.Outcome,
            result.LdapErrorCode,
            result.DirectoryDataCode,
            result.NativeErrorCode);
        return result.Outcome switch
        {
            DomainCredentialCheckOutcome.InvalidCredentials => new(false, "DomainJoin.ZeroTouchCredentialsRejected"),
            DomainCredentialCheckOutcome.AccountLocked => new(false, "DomainJoin.ZeroTouchAccountLocked"),
            DomainCredentialCheckOutcome.PasswordExpired => new(false, "DomainJoin.ZeroTouchPasswordExpired"),
            DomainCredentialCheckOutcome.AccountDisabled => new(false, "DomainJoin.ZeroTouchAccountDisabled"),
            _ => ZeroTouchDomainCredentialCheckDecision.Start
        };
    }
}
