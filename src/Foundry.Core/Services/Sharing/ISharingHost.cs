// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// What <see cref="ConfigurationSharingEngine"/> needs from the application: the current configuration, its readiness, the secret
/// vault and the managed (read-only) state. The WinUI host implements it over the configuration state service and the single vault
/// writer.
/// </summary>
/// <remarks>
/// The engine calls these members from the context its own methods are called on (it does not leave it with
/// <c>ConfigureAwait(false)</c>), never from its background content fetch, so a host that calls the engine on its UI thread is called
/// back on that thread. Vault values are secrets: the host never logs them.
/// </remarks>
public interface ISharingHost
{
    /// <summary>Gets the current configuration, with the per-PC values of this PC.</summary>
    FoundryConfigurationDocument CurrentConfiguration { get; }

    /// <summary>Gets whether the current configuration can build media (the deploy configuration readiness, passwords included).</summary>
    bool IsReadyToBuild { get; }

    /// <summary>
    /// Gets whether the vault was read and can be written: false when it is unreadable (for example after a DPAPI reset, until a
    /// write replaces it) or blocked by an I/O failure for the session. While it is false the entries <see cref="ReadVault"/>
    /// returns are not the whole vault, so the engine never creates replacement reference keys from them.
    /// </summary>
    bool IsVaultAvailable { get; }

    /// <summary>Returns a copy of every vault entry, pending password changes included.</summary>
    IReadOnlyDictionary<string, string> ReadVault();

    /// <summary>
    /// Sets <paramref name="entries"/> in the vault and removes <paramref name="removeIds"/>; every other entry is kept. Configuration
    /// passwords among the entries are also restored into their secret state services. The engine removes sharing ids only.
    /// </summary>
    /// <exception cref="IOException">The vault cannot be written; nothing changed.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the vault is denied; nothing changed.</exception>
    void WriteVault(IReadOnlyDictionary<string, string> entries, IReadOnlyCollection<string>? removeIds = null);

    /// <summary>
    /// Follower: replaces the current configuration with <paramref name="configuration"/> (already carrying the per-PC values of
    /// this PC), then sets <paramref name="vaultEntries"/> in the vault as <see cref="WriteVault"/> does (the publication passwords
    /// overwrite matching ids, other entries are kept; sharing ids are never among them), and marks the configuration managed by
    /// <paramref name="managedBy"/> so published sections are read-only.
    /// </summary>
    /// <remarks>
    /// The host must hold the <c>ConfigurationReplacementGate</c> around the configuration replacement, as import does, so a library
    /// content cleanup cannot delete content the new configuration references. The configuration is replaced before the vault is
    /// written because restored passwords are mapped against it. A vault that cannot be written does not fail the apply: the engine
    /// reports the missing passwords.
    /// </remarks>
    /// <exception cref="IOException">The configuration cannot be saved; the engine keeps the applied sequence and retries at the next check.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the configuration is denied; retried at the next check.</exception>
    Task ApplyConfigurationAsync(FoundryConfigurationDocument configuration, IReadOnlyDictionary<string, string> vaultEntries, string managedBy,
        CancellationToken cancellationToken);

    /// <summary>Follower stops following: the configuration stays as it is and becomes editable again.</summary>
    void ClearManaged();

    /// <summary>
    /// Returns whether every content item <see cref="PortableConfiguration.ListContents"/> lists for <paramref name="configuration"/> is
    /// available in the local libraries: authoring files, packages of enabled post-installation actions, included driver sets and
    /// included custom images.
    /// </summary>
    bool AllContentAvailable(FoundryConfigurationDocument configuration);
}
