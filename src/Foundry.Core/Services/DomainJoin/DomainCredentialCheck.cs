// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Net;
using System.Security;
using System.Text.RegularExpressions;

namespace Foundry.Core.Services.DomainJoin;

/// <summary>Outcome of checking a domain join account and password against a domain controller before the join.</summary>
public enum DomainCredentialCheckOutcome
{
    /// <summary>The domain controller accepted the account and password. OU rights and account quotas are not evaluated.</summary>
    Valid,

    /// <summary>The domain rejected the account name or the password.</summary>
    InvalidCredentials,

    /// <summary>The account is locked out.</summary>
    AccountLocked,

    /// <summary>The password has expired or must be changed at next logon.</summary>
    PasswordExpired,

    /// <summary>The account is disabled or expired.</summary>
    AccountDisabled,

    /// <summary>No domain controller could be located or reached, so the account was not evaluated.</summary>
    ControllerUnavailable,

    /// <summary>The check failed without a reason that proves the account is unusable.</summary>
    Inconclusive
}

/// <summary>
/// Sanitized result of a domain credential check. It carries only numeric codes, never the domain, account,
/// password or server message text.
/// </summary>
/// <param name="Outcome">Classified outcome.</param>
/// <param name="NativeErrorCode">Win32 status returned by the domain controller locator, when it failed.</param>
/// <param name="LdapErrorCode">LDAP result code returned by the bind, when it failed.</param>
/// <param name="DirectoryDataCode">Active Directory <c>data</c> code parsed from the bind failure, when present.</param>
public sealed record DomainCredentialCheckResult(
    DomainCredentialCheckOutcome Outcome,
    int? NativeErrorCode = null,
    int? LdapErrorCode = null,
    int? DirectoryDataCode = null)
{
    /// <summary>Gets whether the directory positively rejected the account, so the deployment must not continue with it.</summary>
    public bool IsRejection => Outcome is DomainCredentialCheckOutcome.InvalidCredentials
        or DomainCredentialCheckOutcome.AccountLocked
        or DomainCredentialCheckOutcome.PasswordExpired
        or DomainCredentialCheckOutcome.AccountDisabled;
}

/// <summary>Maps locator and LDAP bind failures to a credential check outcome without guessing a rejection.</summary>
public static partial class DomainCredentialCheckClassifier
{
    private const int LdapInvalidCredentials = 49;
    private const int LdapServerDown = 81;
    private const int LdapConnectError = 91;

    /// <summary>
    /// Classifies a failed check. A non-zero <paramref name="nativeError"/> means the locator failed; zero means the
    /// locator answered with a controller that does not belong to the requested domain. LDAP 49 blocks only when the
    /// server message carries a known Active Directory <c>data</c> code.
    /// </summary>
    public static DomainCredentialCheckResult Classify(int? nativeError, int? ldapError, string? serverErrorMessage)
    {
        if (nativeError is int native)
        {
            return new(native != 0 ? DomainCredentialCheckOutcome.ControllerUnavailable : DomainCredentialCheckOutcome.Inconclusive,
                NativeErrorCode: native);
        }

        if (ldapError is LdapServerDown or LdapConnectError)
        {
            return new(DomainCredentialCheckOutcome.ControllerUnavailable, LdapErrorCode: ldapError);
        }

        if (ldapError != LdapInvalidCredentials)
        {
            return new(DomainCredentialCheckOutcome.Inconclusive, LdapErrorCode: ldapError);
        }

        int? dataCode = ParseDataCode(serverErrorMessage);
        DomainCredentialCheckOutcome outcome = dataCode switch
        {
            0x52e or 0x525 => DomainCredentialCheckOutcome.InvalidCredentials,
            0x775 => DomainCredentialCheckOutcome.AccountLocked,
            0x532 or 0x773 => DomainCredentialCheckOutcome.PasswordExpired,
            0x533 or 0x701 => DomainCredentialCheckOutcome.AccountDisabled,
            _ => DomainCredentialCheckOutcome.Inconclusive
        };
        return new(outcome, LdapErrorCode: ldapError, DirectoryDataCode: dataCode);
    }

    /// <summary>
    /// Builds the bind credential the same way PostInstall does: <c>DOMAIN\user</c> splits on the first backslash,
    /// any other spelling (including a UPN) is passed as the user name. The credential takes its own copy
    /// of the secret; the caller releases that copy by setting <see cref="NetworkCredential.Password"/> to <see langword="null"/>.
    /// </summary>
    public static NetworkCredential CreateCredential(string accountName, SecureString password)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        ArgumentNullException.ThrowIfNull(password);
        string[] account = accountName.Trim().Split('\\', 2);
        return account.Length == 2 ? new NetworkCredential(account[1], password, account[0]) : new NetworkCredential(account[0], password);
    }

    private static int? ParseDataCode(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        Match match = DataCodePattern().Match(message);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    [GeneratedRegex(@"data ([0-9a-f]{1,8})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DataCodePattern();
}
