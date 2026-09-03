// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public sealed class OobeAccountSecretState : IDisposable
{
    private readonly SecretPair administrator = new();
    private readonly Dictionary<string, SecretPair> additionalAccounts = new(StringComparer.Ordinal);
    private bool isDisposed;

    public void SetAdministratorPassword(string? value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        administrator.SetPassword(value);
    }

    public void SetAdministratorConfirmation(string? value)
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

    public bool IsAdministratorPasswordConfirmed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return administrator.IsConfirmed;
    }

    public void SetAdditionalAccountPassword(string accountId, string? value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        GetOrCreateAccount(accountId).SetPassword(value);
    }

    public void SetAdditionalAccountConfirmation(string accountId, string? value)
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

    public bool IsAdditionalAccountPasswordConfirmed(string accountId)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return !TryGetAccount(accountId, out SecretPair? pair) || pair!.IsConfirmed;
    }

    public void Update(OobeSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            Clear();
            return;
        }

        if (!settings.EnableAdministratorAccount)
        {
            administrator.Clear();
        }

        HashSet<string> activeAccountIds = settings.AdditionalAccounts
            .Where(account => !string.IsNullOrWhiteSpace(account.Id))
            .Select(account => account.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string accountId in additionalAccounts.Keys.ToArray())
        {
            if (activeAccountIds.Contains(accountId))
            {
                continue;
            }

            additionalAccounts[accountId].Clear();
            additionalAccounts.Remove(accountId);
        }
    }

    public OobeAccountConfigurationValidationResult Validate(OobeSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return OobeAccountConfigurationValidator.Validate(settings, this);
    }

    public void Clear()
    {
        administrator.Clear();

        foreach (SecretPair pair in additionalAccounts.Values)
        {
            pair.Clear();
        }

        additionalAccounts.Clear();
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

        public bool IsConfirmed =>
            password is null && confirmation is null ||
            password is not null &&
            confirmation is not null &&
            password.AsSpan().SequenceEqual(confirmation);

        public void SetPassword(string? value)
        {
            Replace(ref password, value);
        }

        public void SetConfirmation(string? value)
        {
            Replace(ref confirmation, value);
        }

        public char[] GetPasswordCopy() => password?.ToArray() ?? [];

        public char[] GetConfirmationCopy() => confirmation?.ToArray() ?? [];

        public void Clear()
        {
            ClearBuffer(ref password);
            ClearBuffer(ref confirmation);
        }

        private static void Replace(ref char[]? target, string? value)
        {
            ClearBuffer(ref target);
            target = value is null ? null : value.ToCharArray();
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
