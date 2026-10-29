// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Configuration;
using Foundry.Services.Secrets;
using Foundry.Services.Sharing;
using Serilog;

namespace Foundry.Services.NetworkCache;

/// <summary>
/// Resolves network cache trust from the configuration sharing role (<see cref="NetworkCacheTrust"/>) and keeps the signing key
/// in the vault entry <see cref="SecretIds.SigningKey"/> as base64 PKCS#8, the key configuration sharing also signs with.
/// </summary>
/// <remarks>
/// Trust reads are read-only and cheap: the vault is read once, and only the public key, or its absence, is cached for the
/// session; readiness checks therefore never import, create or write a key. The key is created, only on a standalone PC (see
/// <see cref="NetworkCacheTrust.CanCreateSigningKey"/>) whose entry is missing or malformed, at explicit points: <see cref="Initialize"/>, the network cache becoming active,
/// the role turning to a publishing role with an active cache, a vault status change with an active cache, and media creation
/// or publication. A reference PC never creates or replaces it here: configuration sharing owns its key, and without a usable one
/// it trusts nothing and cannot publish. Nothing is written before the vault bridge
/// has loaded the vault, or while it is unreadable, so a write can never replace vault entries that were not read. While sharing
/// is unavailable nothing is trusted: media with an active network cache stay blocked instead of being bound to a key a
/// follower must not use. Key bytes are never logged.
/// </remarks>
internal sealed class NetworkCacheSigningService : INetworkCacheSigningService, IDisposable
{
    private readonly ISecretVaultBridge vault;
    private readonly ConfigurationSharingService sharing;
    private readonly IFoundryConfigurationStateService configuration;
    private readonly ILogger logger;
    private byte[]? ownPublicKey;
    private bool isOwnKeyRead;
    // The vault status when key creation last failed; creation is not retried until the status changes.
    private VaultStatus? creationFailedAt;
    private bool wasCacheActive;
    private SharingRole lastRole;
    private bool wasSharingAvailable;
    private bool isInitialized;
    // Writing the key changes the vault status, which must not re-enter key creation.
    private bool isEnsuringKey;

    public NetworkCacheSigningService(
        ISecretVaultBridge vault,
        ConfigurationSharingService sharing,
        IFoundryConfigurationStateService configuration,
        ILogger logger)
    {
        this.vault = vault;
        this.sharing = sharing;
        this.configuration = configuration;
        this.logger = logger.ForContext<NetworkCacheSigningService>();
    }

    /// <inheritdoc />
    public bool CanPublishIndex => sharing.IsAvailable && NetworkCacheTrust.CanPublish(sharing.Engine.Role);

    /// <inheritdoc />
    public bool CanSignIndex => CanPublishIndex && (ReadOwnPublicKey() is not null || CanCreateKey);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public byte[]? TrustedPublicKey
    {
        get
        {
            if (!sharing.IsAvailable) return null;
            SharingState state = sharing.Engine.State;
            byte[]? own = NetworkCacheTrust.CanPublish(state.Role) ? ReadOwnPublicKey() : null;
            return NetworkCacheTrust.Resolve(state.Role, own, state.PinnedPublicKey) is { } trust
                ? (byte[])trust.PublicKey.Clone()
                : null;
        }
    }

    /// <inheritdoc />
    public NetworkCacheIndexBinding? GetBinding(long minimumSequence) =>
        TrustedPublicKey is { } publicKey ? new NetworkCacheIndexBinding(publicKey, minimumSequence) : null;

    /// <inheritdoc />
    public void Initialize()
    {
        if (isInitialized) return;
        isInitialized = true;
        wasCacheActive = IsCacheActive;
        lastRole = sharing.Role;
        wasSharingAvailable = sharing.IsAvailable;
        configuration.StateChanged += OnConfigurationChanged;
        sharing.Changed += OnSharingChanged;
        vault.StatusChanged += OnVaultStatusChanged;
        if (wasCacheActive) EnsureSigningKey();
    }

    /// <inheritdoc />
    public bool EnsureSigningKey()
    {
        if (!CanPublishIndex) return false;
        isOwnKeyRead = false;
        if (ReadOwnPublicKey() is not null) return true;
        if (!CanCreateKey) return false;

        isEnsuringKey = true;
        try
        {
            using PublicationSigningKey created = CreateAndStore();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            creationFailedAt = vault.Status;
            logger.Warning(exception, "Network cache signing key could not be created; media with the network cache on cannot be created.");
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }
        finally
        {
            isEnsuringKey = false;
        }
    }

    /// <inheritdoc />
    public PublicationSigningKey GetOrCreateSigningKey()
    {
        if (!CanPublishIndex) throw new InvalidOperationException("Only a reference or standalone PC signs the network cache index.");
        if (!CanWriteVault) throw new InvalidOperationException("The secret vault is not loaded or is unreadable.");

        if (vault.ReadEntries().TryGetValue(SecretIds.SigningKey, out string? encoded) && Import(encoded) is { } existing)
        {
            ownPublicKey = existing.PublicKey;
            isOwnKeyRead = true;
            return existing;
        }

        if (!NetworkCacheTrust.CanCreateSigningKey(sharing.Engine.Role))
            throw new InvalidOperationException("The reference PC has no signing key that can be read; configuration sharing creates it.");
        return CreateAndStore();
    }

    public void Dispose()
    {
        if (!isInitialized) return;
        configuration.StateChanged -= OnConfigurationChanged;
        sharing.Changed -= OnSharingChanged;
        vault.StatusChanged -= OnVaultStatusChanged;
    }

    private bool IsCacheActive => NetworkCacheContentPublisher.IsActive(configuration.Current.NetworkCache);

    private bool CanWriteVault => vault.IsLoaded && vault.Status != VaultStatus.Unreadable;

    // Only a standalone PC creates its key, and only into a vault that can take it and did not refuse it at its current status.
    private bool CanCreateKey => NetworkCacheTrust.CanCreateSigningKey(sharing.Engine.Role) && CanWriteVault && creationFailedAt != vault.Status;

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        bool isActive = IsCacheActive;
        bool becameActive = isActive && !wasCacheActive;
        wasCacheActive = isActive;
        if (becameActive) EnsureSigningKey();
    }

    /// <summary>
    /// Follows the role and the availability of sharing, which decide trust (a pinned key on a follower, the own key otherwise);
    /// progress and other sharing changes are ignored.
    /// </summary>
    private void OnSharingChanged(object? sender, EventArgs e)
    {
        SharingRole role = sharing.Role;
        bool isAvailable = sharing.IsAvailable;
        if (role == lastRole && isAvailable == wasSharingAvailable) return;
        lastRole = role;
        wasSharingAvailable = isAvailable;
        if (CanPublishIndex && IsCacheActive) EnsureSigningKey();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A vault that becomes readable or loaded can now hold or receive the key, so creation is tried again (a previous failure
    /// is retried only on a new status), and readiness re-evaluates.
    /// </summary>
    private void OnVaultStatusChanged(object? sender, EventArgs e)
    {
        if (isEnsuringKey) return;
        isOwnKeyRead = false;
        if (IsCacheActive) EnsureSigningKey();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the public key of the stored signing key, reading the vault once; never writes.</summary>
    private byte[]? ReadOwnPublicKey()
    {
        if (isOwnKeyRead) return ownPublicKey;
        // Before the vault is loaded it reads as empty; the absence is not cached so the key is found once it is loaded.
        if (!vault.IsLoaded) return null;

        isOwnKeyRead = true;
        ownPublicKey = null;
        if (vault.ReadEntries().TryGetValue(SecretIds.SigningKey, out string? encoded) && Import(encoded) is { } key)
        {
            using (key) ownPublicKey = key.PublicKey;
        }

        return ownPublicKey;
    }

    private PublicationSigningKey CreateAndStore()
    {
        PublicationSigningKey created = PublicationSigningKey.Create();
        byte[] pkcs8 = created.ExportPkcs8();
        try
        {
            vault.WriteEntries(new Dictionary<string, string>(StringComparer.Ordinal) { [SecretIds.SigningKey] = Convert.ToBase64String(pkcs8) });
        }
        catch
        {
            created.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        ownPublicKey = created.PublicKey;
        isOwnKeyRead = true;
        creationFailedAt = null;
        logger.Information("Network cache signing key created. KeyId={KeyId}", created.KeyId);
        Changed?.Invoke(this, EventArgs.Empty);
        return created;
    }

    private PublicationSigningKey? Import(string encoded)
    {
        byte[]? pkcs8 = null;
        try
        {
            pkcs8 = Convert.FromBase64String(encoded);
            return PublicationSigningKey.Import(pkcs8);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            logger.Warning("The signing key in the secret vault is malformed; a standalone PC replaces it when the key is next needed.");
            return null;
        }
        finally
        {
            if (pkcs8 is not null) CryptographicOperations.ZeroMemory(pkcs8);
        }
    }
}
