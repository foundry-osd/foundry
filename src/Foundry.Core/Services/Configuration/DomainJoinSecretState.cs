// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Owns one session password bound to an active automatic domain credential context.</summary>
public sealed class DomainJoinSecretState : IDisposable
{
    private char[]? password;
    private DomainJoinCredentialContext? context;
    private bool isDisposed;

    /// <summary>Validates and copies a password without normalization, clearing the previous owned buffer.</summary>
    public void SetPassword(DomainJoinCredentialContext context, ReadOnlySpan<char> value)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        DomainJoinCredentialPayloadCodec.ValidateContext(context);
        if (!value.IsEmpty) DomainJoinCredentialPayloadCodec.ValidatePassword(value);
        Clear();
        this.context = context;
        password = value.IsEmpty ? null : value.ToArray();
    }

    /// <summary>Returns an independent owned copy only for matching identity; callers must clear it.</summary>
    public char[]? GetPasswordCopy(DomainJoinCredentialContext context)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return HasPassword(context) ? password!.ToArray() : null;
    }

    /// <summary>Reports availability only for the same canonical domain and account.</summary>
    public bool HasPassword(DomainJoinCredentialContext context)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(context);
        return password is { Length: > 0 } && this.context?.Matches(context) == true;
    }

    /// <summary>Clears credentials immediately when automatic mode or their domain/account ownership changes.</summary>
    public bool Update(DomainJoinSettings settings)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        return !settings.IsEnabled || settings.Mode != DomainJoinMode.Automatic ||
            context?.Matches(new(settings.DomainName ?? string.Empty, settings.AccountName ?? string.Empty)) != true
            ? Clear() : false;
    }

    /// <summary>Erases the owned password and its binding; independent caller copies remain caller-owned.</summary>
    public bool Clear()
    {
        bool changed = password is not null;
        if (password is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        password = null;
        context = null;
        return changed;
    }

    public void Dispose()
    {
        if (isDisposed) return;
        Clear();
        isDisposed = true;
    }
}
