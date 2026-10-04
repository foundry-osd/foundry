// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>Owns one private password copy until target staging or a terminal launch/execution exit.</summary>
public sealed class DomainJoinPreparedInput : IDisposable
{
    private char[]? password;

    public DomainJoinPreparedInput(DomainJoinCredentialContext credentialContext, string computerName,
        string? targetOuDn, ReadOnlySpan<char> password)
    {
        if (!DomainJoinCredentialContext.IsValidDomainName(credentialContext.DomainName) ||
            !DomainJoinConfigurationValidator.IsQualifiedAccount(credentialContext.AccountName) ||
            !ComputerNameRules.IsValid(computerName) ||
            targetOuDn is not null && !DistinguishedNameRules.IsWithinDomain(targetOuDn, credentialContext.DomainName))
            throw new ArgumentException("The prepared domain input is invalid.");
        byte[] validated = DomainJoinCredentialPayloadCodec.Encode(credentialContext, password);
        CryptographicOperations.ZeroMemory(validated);
        CredentialContext = credentialContext;
        ComputerName = computerName;
        TargetOuDn = targetOuDn;
        this.password = password.ToArray();
    }

    /// <summary>Gets authenticated credential ownership for the binary target payload; never persist this as diagnostics.</summary>
    internal DomainJoinCredentialContext CredentialContext { get; }
    /// <summary>Provides staging access only while this owner remains alive; callers must not retain or serialize it.</summary>
    internal ReadOnlyMemory<char> Password => password ?? throw new ObjectDisposedException(nameof(DomainJoinPreparedInput));
    public string ComputerName { get; }
    public string? TargetOuDn { get; }

    /// <summary>Compares frozen metadata without exposing the account or password to durable state.</summary>
    internal bool Matches(DomainJoinDeploymentIntent intent) =>
        password is not null &&
        string.Equals(DomainJoinCredentialContext.CanonicalizeDomainName(CredentialContext.DomainName),
            DomainJoinCredentialContext.CanonicalizeDomainName(intent.DomainName), StringComparison.Ordinal) &&
        string.Equals(ComputerName, intent.ComputerName, StringComparison.Ordinal) &&
        string.Equals(TargetOuDn, intent.TargetOuDn, StringComparison.Ordinal);

    /// <summary>Rejects missing, foreign or contradictory ownership before any execution workspace or disk mutation.</summary>
    internal static bool IsValidFor(DeploymentContext request, DomainJoinPreparedInput? input)
    {
        if (request.DomainJoinRequest is null) return request.DomainJoinIntent is null && input is null;
        if (request.IsAutopilotEnabled || !Enum.IsDefined(request.DomainJoinRequest.Mode)) return false;
        return request.DomainJoinRequest.Disposition switch
        {
            DomainJoinDeploymentDisposition.UnsupportedEdition => input is null && request.DomainJoinIntent is null,
            DomainJoinDeploymentDisposition.DryRun => input is null && request.IsDryRun,
            DomainJoinDeploymentDisposition.Ready => !request.IsDryRun && request.DomainJoinIntent is { } intent &&
                DomainJoinCredentialContext.IsValidDomainName(intent.DomainName) && input?.Matches(intent) == true &&
                (request.UsesCustomUnattend || string.Equals(request.TargetComputerName, intent.ComputerName, StringComparison.Ordinal)),
            _ => false
        };
    }

    public void Dispose()
    {
        char[]? owned = Interlocked.Exchange(ref password, null);
        if (owned is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(owned.AsSpan()));
    }
}
