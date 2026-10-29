// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Secrets;

namespace Foundry.Services.Secrets;

/// <summary>
/// The single writer of the local secret vault: reports whether the remembered passwords could be restored, and serves the vault
/// entries to export, import and sharing.
/// </summary>
/// <remarks>Members run on the UI thread, which owns the secret state services the bridge restores into.</remarks>
public interface ISecretVaultBridge
{
    /// <summary>
    /// Gets the vault state: <see cref="VaultStatus.Unreadable"/> means the saved passwords could not be restored and must be
    /// entered again; it becomes <see cref="VaultStatus.Ok"/> once a password is saved again.
    /// </summary>
    VaultStatus Status { get; }

    /// <summary>
    /// Gets whether startup has loaded the vault. Before that, <see cref="ReadEntries"/> returns no entries, and a write would replace
    /// the vault with only the written entries, so callers that write on their own initiative wait for it.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>Raised on the UI thread when startup has loaded the vault and whenever <see cref="Status"/> changes afterwards.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Returns every vault entry, including the ids the bridge does not own, after saving pending password changes.</summary>
    /// <remarks>An unreadable vault, or one that could not be read at startup, returns no entries: check <see cref="Status"/>.</remarks>
    IReadOnlyDictionary<string, string> ReadEntries();

    /// <summary>
    /// Writes <paramref name="entries"/> into the vault and removes <paramref name="removeIds"/>, keeping every other entry; the
    /// owned passwords among <paramref name="entries"/> are also pushed into the secret state services.
    /// </summary>
    /// <remarks>
    /// Call after the configuration that owns the passwords is current: an owned password whose owner is not in the configuration
    /// is dropped at the next save. <paramref name="removeIds"/> accepts only ids <see cref="SecretMap"/> does not own.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="removeIds"/> contains an id owned by <see cref="SecretMap"/>.</exception>
    /// <exception cref="IOException">The vault could not be read at startup or cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the vault is denied.</exception>
    /// <exception cref="CryptographicException">The vault content cannot be protected.</exception>
    void WriteEntries(IReadOnlyDictionary<string, string> entries, IEnumerable<string>? removeIds = null);
}
