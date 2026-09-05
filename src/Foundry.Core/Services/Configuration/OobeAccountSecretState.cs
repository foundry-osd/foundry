// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Holds session-only account secrets in owned buffers that are cleared when replaced or discarded.
/// </summary>
public sealed class OobeAccountSecretState : IDisposable
{
    private readonly SecretPair administrator = new();
    private readonly Dictionary<string, SecretPair> additionalAccounts = new(StringComparer.Ordinal);
    private bool isDisposed;

    public void SetAdministratorPassword(ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        administrator.SetPassword(value);
    }

    public void SetAdministratorConfirmation(ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        administrator.SetConfirmation(value);
    }

    public char[] GetAdministratorPasswordCopy()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return administrator.GetPasswordCopy();
    }

    public char[] GetAdministratorConfirmationCopy()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return administrator.GetConfirmationCopy();
    }

    internal bool HasAdministratorPassword => administrator.HasPassword;

    public void SetAdditionalAccountPassword(string accountId, ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        GetOrCreateAccount(accountId).SetPassword(value);
    }

    public void SetAdditionalAccountConfirmation(string accountId, ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        GetOrCreateAccount(accountId).SetConfirmation(value);
    }

    public char[] GetAdditionalAccountPasswordCopy(string accountId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return TryGetAccount(accountId, out SecretPair? pair)
            ? pair!.GetPasswordCopy()
            : [];
    }

    public char[] GetAdditionalAccountConfirmationCopy(string accountId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return TryGetAccount(accountId, out SecretPair? pair)
            ? pair!.GetConfirmationCopy()
            : [];
    }

    internal bool HasAdditionalAccountPassword(string accountId)
    {
        return TryGetAccount(accountId, out SecretPair? pair) && pair!.HasPassword;
    }

    /// <summary>
    /// Discards secrets for disabled or removed accounts and reports whether any secret state was cleared.
    /// </summary>
    public bool Update(OobeSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            return Clear();
        }

        bool changed = false;
        if (!settings.EnableAdministratorAccount || !settings.UseAdministratorPassword)
        {
            changed = administrator.Clear();
        }

        Dictionary<string, bool> activeAccountPasswordModes = settings.AdditionalAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Id))
            .GroupBy(account => account.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().UsePassword, StringComparer.Ordinal);

        foreach (string accountId in additionalAccounts.Keys.ToArray())
        {
            if (!activeAccountPasswordModes.TryGetValue(accountId, out bool usePassword) || !usePassword)
            {
                additionalAccounts[accountId].Clear();
                additionalAccounts.Remove(accountId);
                changed = true;
            }
        }

        return changed;
    }

    public OobeAccountConfigurationValidationResult Validate(OobeSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return OobeAccountConfigurationValidator.Validate(settings, this);
    }

    public bool Clear()
    {
        bool changed = administrator.Clear();

        foreach (SecretPair pair in additionalAccounts.Values)
        {
            changed |= pair.Clear();
        }

        additionalAccounts.Clear();
        return changed;
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        Clear();
        isDisposed = true;
    }

    internal bool HasAdministratorPasswordConfirmationMismatch =>
        !administrator.IsConfirmed;

    internal bool HasAdditionalAccountPasswordConfirmationMismatch(string accountId)
    {
        return TryGetAccount(accountId, out SecretPair? pair) && !pair!.IsConfirmed;
    }

    private SecretPair GetOrCreateAccount(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        if (!additionalAccounts.TryGetValue(accountId, out SecretPair? pair))
        {
            pair = new SecretPair();
            additionalAccounts.Add(accountId, pair);
        }

        return pair;
    }

    private bool TryGetAccount(string accountId, out SecretPair? pair)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return additionalAccounts.TryGetValue(accountId, out pair);
    }

    private sealed class SecretPair
    {
        private char[]? password;
        private char[]? confirmation;

        public bool HasPassword => password is { Length: > 0 };

        public bool IsConfirmed => password.AsSpan().SequenceEqual(confirmation);

        public void SetPassword(ReadOnlySpan<char> value)
        {
            Replace(ref password, value);
        }

        public void SetConfirmation(ReadOnlySpan<char> value)
        {
            Replace(ref confirmation, value);
        }

        public char[] GetPasswordCopy() => password?.ToArray() ?? [];

        public char[] GetConfirmationCopy() => confirmation?.ToArray() ?? [];

        public bool Clear()
        {
            bool changed = password is not null || confirmation is not null;
            ClearBuffer(ref password);
            ClearBuffer(ref confirmation);
            return changed;
        }

        private static void Replace(ref char[]? target, ReadOnlySpan<char> value)
        {
            ClearBuffer(ref target);
            target = value.IsEmpty ? null : value.ToArray();
        }

        private static void ClearBuffer(ref char[]? value)
        {
            if (value is null)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(value.AsSpan()));
            value = null;
        }
    }
}
