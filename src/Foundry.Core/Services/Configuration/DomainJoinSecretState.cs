// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Owns the session passwords of the join accounts, one per account. Two domains that name the same account share
/// one password; the domain is bound later, when a payload is written for the media.
/// </summary>
public sealed class DomainJoinSecretState : IDisposable
{
    private readonly Dictionary<string, char[]> passwords = new(StringComparer.Ordinal);
    private bool isDisposed;

    /// <summary>Gets the canonical names of the accounts that currently own a password.</summary>
    public IReadOnlyList<string> AccountNames
    {
        get
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return passwords.Keys.ToArray();
        }
    }

    /// <summary>Validates and copies a password without normalization; an empty value removes the account's password.</summary>
    public void SetPassword(string accountName, ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (!DomainJoinConfigurationValidator.IsQualifiedAccount(accountName))
            throw new ArgumentException("Only a qualified account can own a password.", nameof(accountName));
        if (!value.IsEmpty) DomainJoinCredentialPayloadCodec.ValidatePassword(value);
        string key = DomainJoinCredentialContext.CanonicalizeAccountName(accountName);
        Erase(key);
        if (!value.IsEmpty) passwords[key] = value.ToArray();
    }

    /// <summary>Returns an independent owned copy for the account; callers must clear it.</summary>
    public char[]? GetPasswordCopy(string accountName)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return Find(accountName)?.ToArray();
    }

    /// <summary>Reports whether the account, compared in canonical form, owns a password.</summary>
    public bool HasPassword(string accountName)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return Find(accountName) is not null;
    }

    /// <summary>Erases the passwords of accounts no listed domain joins with any more; returns whether any was erased.</summary>
    public bool Update(DomainJoinSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        var referenced = new HashSet<string>(settings.GetReferencedAccountNames(), StringComparer.Ordinal);
        string[] orphaned = passwords.Keys.Where(account => !referenced.Contains(account)).ToArray();
        foreach (string account in orphaned) Erase(account);
        return orphaned.Length > 0;
    }

    /// <summary>Erases every owned password; independent caller copies remain caller-owned.</summary>
    public bool Clear()
    {
        bool changed = passwords.Count > 0;
        foreach (string account in passwords.Keys.ToArray()) Erase(account);
        return changed;
    }

    public void Dispose()
    {
        if (isDisposed) return;
        Clear();
        isDisposed = true;
    }

    private char[]? Find(string accountName) =>
        !string.IsNullOrWhiteSpace(accountName) &&
        passwords.TryGetValue(DomainJoinCredentialContext.CanonicalizeAccountName(accountName), out char[]? password) ? password : null;

    private void Erase(string key)
    {
        if (!passwords.Remove(key, out char[]? password)) return;
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
    }
}
