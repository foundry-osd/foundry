// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Holds the session-only network secrets resolved while loading the Foundry.Connect configuration.
/// </summary>
/// <remarks>
/// The Core runtime configuration carries secrets only as encrypted envelopes. Decrypted values and the legacy
/// plaintext Wi-Fi passphrase of older media live here instead, so they never round-trip through the shared
/// configuration contract. Each secret has its own availability: one that cannot be read disables only the feature
/// that needs it (D9). Secrets protected by the deployment key stay <see cref="ConnectSecretStatus.Locked"/> until one
/// <see cref="Unlock"/> decrypts all of them (L4-12). This is deliberately a class whose <c>ToString</c> prints no value.
/// </remarks>
public sealed class ConnectRuntimeSecrets
{
    private volatile bool _isUnlockDeclined;

    /// <summary>
    /// Initializes the secrets holder.
    /// </summary>
    /// <param name="wifiPassphrase">The personal Wi-Fi passphrase.</param>
    /// <param name="wifiCertificatePfxPassword">The PFX password of the enterprise Wi-Fi client certificate.</param>
    /// <param name="wiredCertificatePfxPassword">The PFX password of the wired 802.1X client certificate.</param>
    /// <param name="wiredClientCertificate">The encrypted wired 802.1X client PFX, or <see langword="null"/> when it is not staged encrypted.</param>
    /// <param name="wifiClientCertificate">The encrypted enterprise Wi-Fi client PFX, or <see langword="null"/> when it is not staged encrypted.</param>
    /// <param name="wifiEnterpriseProfile">The encrypted enterprise Wi-Fi template, or <see langword="null"/> when it is not staged encrypted.</param>
    /// <param name="protection">The deployment password protection block that unlocks the deployment key, when the media has one.</param>
    public ConnectRuntimeSecrets(
        ConnectSecret wifiPassphrase,
        ConnectSecret wifiCertificatePfxPassword,
        ConnectSecret wiredCertificatePfxPassword,
        ConnectProtectedContent? wiredClientCertificate = null,
        ConnectProtectedContent? wifiClientCertificate = null,
        ConnectProtectedContent? wifiEnterpriseProfile = null,
        DeployProtectionSettings? protection = null)
    {
        ArgumentNullException.ThrowIfNull(wifiPassphrase);
        ArgumentNullException.ThrowIfNull(wifiCertificatePfxPassword);
        ArgumentNullException.ThrowIfNull(wiredCertificatePfxPassword);
        WifiPassphrase = wifiPassphrase;
        WifiCertificatePfxPassword = wifiCertificatePfxPassword;
        WiredCertificatePfxPassword = wiredCertificatePfxPassword;
        WiredClientCertificate = wiredClientCertificate ?? ConnectProtectedContent.NotProvisioned();
        WifiClientCertificate = wifiClientCertificate ?? ConnectProtectedContent.NotProvisioned();
        WifiEnterpriseProfile = wifiEnterpriseProfile ?? ConnectProtectedContent.NotProvisioned();
        Protection = protection;
    }

    /// <summary>
    /// Gets the personal Wi-Fi passphrase, decrypted from the media envelope or read from legacy plaintext media.
    /// </summary>
    public ConnectSecret WifiPassphrase { get; }

    /// <summary>
    /// Gets the decrypted PFX password of the enterprise Wi-Fi client certificate.
    /// </summary>
    public ConnectSecret WifiCertificatePfxPassword { get; }

    /// <summary>
    /// Gets the decrypted PFX password of the wired 802.1X client certificate.
    /// </summary>
    public ConnectSecret WiredCertificatePfxPassword { get; }

    /// <summary>
    /// Gets the wired 802.1X client PFX bytes when the media stages it encrypted; <see cref="ConnectSecretStatus.NotProvisioned"/>
    /// when the certificate is absent or a plain file read through its path (schema 5).
    /// </summary>
    public ConnectProtectedContent WiredClientCertificate { get; }

    /// <summary>
    /// Gets the enterprise Wi-Fi client PFX bytes when the media stages it encrypted; <see cref="ConnectSecretStatus.NotProvisioned"/>
    /// otherwise.
    /// </summary>
    public ConnectProtectedContent WifiClientCertificate { get; }

    /// <summary>
    /// Gets the enterprise Wi-Fi profile template bytes when the media stages it encrypted because it carries key material;
    /// <see cref="ConnectSecretStatus.NotProvisioned"/> otherwise.
    /// </summary>
    public ConnectProtectedContent WifiEnterpriseProfile { get; }

    /// <summary>
    /// Gets the deployment password protection block that unwraps the deployment key, or <see langword="null"/> when the media has none.
    /// </summary>
    public DeployProtectionSettings? Protection { get; }

    /// <summary>
    /// Gets whether a wired 802.1X secret (the PFX password or the PFX content) is still locked by the deployment key.
    /// </summary>
    public bool IsWiredLocked =>
        IsLocked(WiredCertificatePfxPassword) || IsLocked(WiredClientCertificate);

    /// <summary>
    /// Gets whether a provisioned Wi-Fi secret (the passphrase, the PFX password, the PFX content or the template) is still locked
    /// by the deployment key.
    /// </summary>
    public bool IsWifiLocked =>
        IsLocked(WifiPassphrase) || IsLocked(WifiCertificatePfxPassword) || IsLocked(WifiClientCertificate) || IsLocked(WifiEnterpriseProfile);

    /// <summary>
    /// Gets whether the technician cancelled the deployment password prompt during this session; locked secrets then stay locked
    /// and are not prompted for again automatically (L4-10).
    /// </summary>
    public bool IsUnlockDeclined => _isUnlockDeclined;

    /// <summary>
    /// Creates a holder in which no secret is provisioned.
    /// </summary>
    public static ConnectRuntimeSecrets None() =>
        new(ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

    /// <summary>
    /// Decrypts every locked secret and content with the unlocked deployment key; items in another state are left as they are.
    /// </summary>
    /// <param name="deploymentKey">The deployment key; the caller keeps ownership and zeroes it.</param>
    /// <returns>How many items became <see cref="ConnectSecretStatus.Available"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="deploymentKey"/> is not a 32-byte key; nothing is changed.</exception>
    public int Unlock(byte[] deploymentKey)
    {
        ConnectSecret.ValidateDeploymentKey(deploymentKey);
        int unlocked = 0;
        foreach (ConnectSecret secret in (ReadOnlySpan<ConnectSecret>)[WifiPassphrase, WifiCertificatePfxPassword, WiredCertificatePfxPassword])
        {
            unlocked += secret.TryUnlock(deploymentKey) ? 1 : 0;
        }

        foreach (ConnectProtectedContent content in (ReadOnlySpan<ConnectProtectedContent>)[WiredClientCertificate, WifiClientCertificate, WifiEnterpriseProfile])
        {
            unlocked += content.TryUnlock(deploymentKey) ? 1 : 0;
        }

        return unlocked;
    }

    /// <summary>
    /// Records that the technician cancelled the deployment password prompt.
    /// </summary>
    public void MarkUnlockDeclined() => _isUnlockDeclined = true;

    /// <summary>
    /// Zeroes every available secret and content.
    /// </summary>
    public void ClearAll()
    {
        WifiPassphrase.Clear();
        WifiCertificatePfxPassword.Clear();
        WiredCertificatePfxPassword.Clear();
        WiredClientCertificate.Clear();
        WifiClientCertificate.Clear();
        WifiEnterpriseProfile.Clear();
    }

    /// <inheritdoc />
    public override string ToString() => nameof(ConnectRuntimeSecrets);

    private static bool IsLocked(ConnectSecret secret) => secret.Status == ConnectSecretStatus.Locked;

    private static bool IsLocked(ConnectProtectedContent content) => content.Status == ConnectSecretStatus.Locked;
}
