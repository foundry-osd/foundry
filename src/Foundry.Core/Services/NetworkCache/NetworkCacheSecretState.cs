// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Holds network cache account passwords in memory by account id; disposal erases them.</summary>
public sealed class NetworkCacheSecretState : IDisposable
{
    private readonly Dictionary<string, char[]> passwords = new(StringComparer.Ordinal);
    private bool isDisposed;

    /// <summary>Validates and copies a password; an empty value removes the account's password.</summary>
    public void SetPassword(string accountId, ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (!value.IsEmpty && (value.Contains('\0') || System.Text.Encoding.UTF8.GetByteCount(value) > NetworkShareCredentialPayloadCodec.MaximumPasswordUtf8Bytes))
            throw new InvalidDataException("The network share password is invalid.");
        Erase(accountId);
        if (!value.IsEmpty) passwords[accountId] = value.ToArray();
    }

    /// <summary>Returns an independent owned copy; callers must clear it.</summary>
    public char[]? GetPasswordCopy(string accountId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return passwords.TryGetValue(accountId, out char[]? password) ? password.ToArray() : null;
    }

    public bool HasPassword(string accountId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return passwords.ContainsKey(accountId);
    }

    /// <summary>
    /// Erases the passwords of accounts the configuration no longer has; returns whether any was erased. Disabling the
    /// feature keeps them, so enabling it again needs no retyping.
    /// </summary>
    public bool Update(NetworkCacheSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        var stored = new HashSet<string>(settings.Accounts.Select(account => account.Id), StringComparer.Ordinal);
        string[] orphaned = passwords.Keys.Where(id => !stored.Contains(id)).ToArray();
        foreach (string id in orphaned) Erase(id);
        return orphaned.Length > 0;
    }

    /// <summary>Erases every owned password; caller copies remain caller-owned.</summary>
    public bool Clear()
    {
        bool changed = passwords.Count > 0;
        foreach (string id in passwords.Keys.ToArray()) Erase(id);
        return changed;
    }

    public void Dispose()
    {
        if (isDisposed) return;
        Clear();
        isDisposed = true;
    }

    private void Erase(string accountId)
    {
        if (!passwords.Remove(accountId, out char[]? password)) return;
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
    }
}
