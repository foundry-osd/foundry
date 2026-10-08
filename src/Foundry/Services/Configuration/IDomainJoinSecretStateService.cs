// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Provides the volatile passwords of the join accounts, one per account, without persisting them in authoring configuration.</summary>
public interface IDomainJoinSecretStateService
{
    event EventHandler? Changed;
    /// <summary>Stores the password of a qualified account; an empty value removes it.</summary>
    void SetPassword(string accountName, ReadOnlySpan<char> value);
    /// <summary>Returns an owned copy for the account. The caller must clear the buffer.</summary>
    char[]? GetPasswordCopy(string accountName);
    bool HasPassword(string accountName);
    void Update(DomainJoinSettings settings);
    void Clear();
}
