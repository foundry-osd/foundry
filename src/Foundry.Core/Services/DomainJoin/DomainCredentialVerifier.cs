// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.DirectoryServices.Protocols;
using System.Net;
using System.Security;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.DomainJoin;

/// <summary>Checks a domain join account and password against the domain before the deployment uses them.</summary>
public interface IDomainCredentialVerifier
{
    /// <summary>
    /// Binds to a writable controller of the context's domain with the supplied password. A <see cref="DomainCredentialCheckOutcome.Valid"/>
    /// result proves only the account and password: OU permissions, the computer account quota and existing computer account
    /// ownership are still evaluated at join time. The password is never logged, stored or included in the result.
    /// </summary>
    Task<DomainCredentialCheckResult> VerifyAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password, CancellationToken token);
}

/// <summary>
/// Performs a signed and sealed Negotiate LDAP bind with the same settings PostInstall uses for the join readiness read.
/// The bind is bounded by a 10 second timeout; cancellation is observed before locating the controller and before binding.
/// </summary>
public sealed class DomainCredentialVerifier : IDomainCredentialVerifier
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public Task<DomainCredentialCheckResult> VerifyAsync(DomainJoinCredentialContext context, ReadOnlyMemory<char> password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.Run(() => Verify(context, password, token), token);
    }

    private static DomainCredentialCheckResult Verify(DomainJoinCredentialContext context, ReadOnlyMemory<char> password, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string domain = DomainJoinCredentialContext.CanonicalizeDomainName(context.DomainName);
        if (!DomainControllerLocator.TryLocate(domain, forceRediscovery: false, out string controller, out int nativeError))
        {
            return DomainCredentialCheckClassifier.Classify(nativeError, null, null);
        }

        token.ThrowIfCancellationRequested();
        using var secret = new SecureString();
        foreach (char character in password.Span) secret.AppendChar(character);
        secret.MakeReadOnly();
        NetworkCredential credential = DomainCredentialCheckClassifier.CreateCredential(context.AccountName, secret);
        try
        {
            using var connection = new LdapConnection(new LdapDirectoryIdentifier(controller, 389, true, false), credential, AuthType.Negotiate);
            connection.Timeout = RequestTimeout;
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.SessionOptions.AutoReconnect = false;
            connection.SessionOptions.SendTimeout = RequestTimeout;
            connection.Bind();
            return new(DomainCredentialCheckOutcome.Valid);
        }
        catch (LdapException error)
        {
            return DomainCredentialCheckClassifier.Classify(null, error.ErrorCode, error.ServerErrorMessage);
        }
        catch (Exception error) when (error is DirectoryException or InvalidOperationException)
        {
            // Not a directory answer about the account, so it never blocks the technician.
            return new(DomainCredentialCheckOutcome.Inconclusive);
        }
        finally { credential.Password = null; }
    }
}
