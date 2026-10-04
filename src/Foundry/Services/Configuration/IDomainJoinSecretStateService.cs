// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>Provides volatile context-bound domain credentials without persisting them in authoring configuration.</summary>
public interface IDomainJoinSecretStateService
{
    event EventHandler? Changed;
    void SetPassword(DomainJoinCredentialContext context, ReadOnlySpan<char> value);
    /// <summary>Returns an owned copy for matching identity. The caller must clear the buffer.</summary>
    char[]? GetPasswordCopy(DomainJoinCredentialContext context);
    bool HasPassword(DomainJoinCredentialContext context);
    void Update(DomainJoinSettings settings);
    void Clear();
}
