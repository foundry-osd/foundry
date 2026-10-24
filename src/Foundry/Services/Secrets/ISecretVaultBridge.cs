// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Secrets;

namespace Foundry.Services.Secrets;

/// <summary>Reports whether the passwords remembered in the local secret vault could be restored.</summary>
public interface ISecretVaultBridge
{
    /// <summary>
    /// Gets the vault state: <see cref="VaultStatus.Unreadable"/> means the saved passwords could not be restored and must be
    /// entered again; it becomes <see cref="VaultStatus.Ok"/> once a password is saved again.
    /// </summary>
    VaultStatus Status { get; }

    /// <summary>Occurs when <see cref="Status"/> changes.</summary>
    event EventHandler? StatusChanged;
}
