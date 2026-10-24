// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Secrets;

/// <summary>Single source of the logical secret ids used as keys of <see cref="LocalSecretVault"/>.</summary>
/// <remarks>Ids are <c>&lt;section&gt;/&lt;name&gt;</c>. A section that needs per-item ids adds its own factory here.</remarks>
public static class SecretIds
{
    private const string SharingPrefix = "sharing/";

    /// <summary>Password protecting deployment media.</summary>
    public const string DeploymentProtection = "deployment/protection";

    /// <summary>Wi-Fi network passphrase.</summary>
    public const string WifiPassphrase = "wifi/passphrase";

    /// <summary>Password of the OOBE administrator account.</summary>
    public const string OobeAdministrator = "oobe/administrator";

    /// <summary>Password protecting published configurations; never shared.</summary>
    public const string PublicationPassword = "sharing/publication-password";

    /// <summary>Private key signing publications; never shared.</summary>
    public const string SigningKey = "sharing/signing-key";

    /// <summary>Master key encrypting published content; never shared.</summary>
    public const string MasterKey = "sharing/master-key";

    /// <summary>
    /// Publication password the current manifest may still be encrypted with while a password change or reset is in flight; removed
    /// once the manifest is rewritten under <see cref="PublicationPassword"/>. Never shared.
    /// </summary>
    public const string PreviousPublicationPassword = "sharing/previous-publication-password";

    /// <summary>
    /// SHA-256 of what the reference published last (folder, portable configuration and included passwords), so an unchanged
    /// configuration is not published again after a restart. Kept in the vault because it is derived from passwords. Never shared.
    /// </summary>
    public const string PublishedFingerprint = "sharing/published-fingerprint";

    /// <summary>Prefix of the Wi-Fi certificate password ids.</summary>
    public const string WifiCertificatePrefix = "wifi/certificate/";

    /// <summary>Prefix of the 802.1X certificate password ids.</summary>
    public const string Dot1xCertificatePrefix = "dot1x/certificate/";

    /// <summary>Prefix of the Autopilot certificate password ids.</summary>
    public const string AutopilotCertificatePrefix = "autopilot/certificate/";

    /// <summary>Prefix of the additional OOBE account password ids.</summary>
    public const string OobeAccountPrefix = "oobe/account/";

    /// <summary>Prefix of the domain join account password ids.</summary>
    public const string DomainAccountPrefix = "domain/";

    /// <summary>Gets the id of the password of a Wi-Fi certificate, keyed by the certificate content hash so it follows its file.</summary>
    public static string WifiCertificate(string hash) => ForHash(WifiCertificatePrefix, hash);

    /// <summary>Gets the id of the password of an 802.1X certificate, keyed by the certificate content hash.</summary>
    public static string Dot1xCertificate(string hash) => ForHash(Dot1xCertificatePrefix, hash);

    /// <summary>Gets the id of the password of an Autopilot certificate, keyed by the certificate content hash.</summary>
    public static string AutopilotCertificate(string hash) => ForHash(AutopilotCertificatePrefix, hash);

    /// <summary>Gets the id of the password of an additional OOBE account.</summary>
    public static string OobeAccount(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return OobeAccountPrefix + accountId;
    }

    /// <summary>Gets the id of the password of a domain join account, keyed by its canonical account name.</summary>
    public static string DomainAccount(string canonicalAccount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAccount);
        return DomainAccountPrefix + canonicalAccount;
    }

    /// <summary>Returns whether the id belongs to the sharing machinery and must never be exported or published.</summary>
    public static bool IsSharingInternal(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return id.StartsWith(SharingPrefix, StringComparison.Ordinal);
    }

    private static string ForHash(string prefix, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        return prefix + hash.ToLowerInvariant();
    }
}
