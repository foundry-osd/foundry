// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Holds network cache account passwords for the session; the secret vault bridge persists them.</summary>
public interface INetworkCacheSecretStateService
{
    event EventHandler? Changed;
    /// <summary>Stores the password of an account by id; an empty value removes it.</summary>
    void SetPassword(string accountId, ReadOnlySpan<char> value);
    /// <summary>Returns an owned copy for the account. The caller must clear the buffer.</summary>
    char[]? GetPasswordCopy(string accountId);
    bool HasPassword(string accountId);
    void Update(NetworkCacheSettings settings);
    void Clear();
}
