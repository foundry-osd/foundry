// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Holds network cache account passwords for the session; the secret vault bridge persists them.</summary>
public interface INetworkCacheSecretStateService
{
    /// <summary>
    /// Raised after <see cref="SetPassword"/> stores or removes a password, and after <see cref="Update"/> erased at least one;
    /// readiness, the vault bridge and the page re-evaluate on it.
    /// </summary>
    event EventHandler? Changed;
    /// <summary>Stores the password of an account by id; an empty value removes it.</summary>
    /// <exception cref="InvalidDataException">The password is not accepted by the network share credential format.</exception>
    void SetPassword(string accountId, ReadOnlySpan<char> value);
    /// <summary>Returns an owned copy for the account. The caller must clear the buffer.</summary>
    char[]? GetPasswordCopy(string accountId);
    /// <summary>Gets whether the session holds a password for the account with this id.</summary>
    bool HasPassword(string accountId);
    /// <summary>
    /// Erases the passwords of accounts <paramref name="settings"/> no longer has, and raises <see cref="Changed"/> only when it
    /// erased one. Turning the network cache off keeps them. Because of this, a new account must be saved in the settings
    /// before its password is stored, or the next update erases the password.
    /// </summary>
    void Update(NetworkCacheSettings settings);
}
