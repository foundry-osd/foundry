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

    /// <summary>Gets the id of the password of a Wi-Fi certificate, keyed by the certificate content hash so it follows its file.</summary>
    public static string WifiCertificate(string hash) => ForHash("wifi/certificate/", hash);

    /// <summary>Gets the id of the password of an 802.1X certificate, keyed by the certificate content hash.</summary>
    public static string Dot1xCertificate(string hash) => ForHash("dot1x/certificate/", hash);

    /// <summary>Gets the id of the password of an Autopilot certificate, keyed by the certificate content hash.</summary>
    public static string AutopilotCertificate(string hash) => ForHash("autopilot/certificate/", hash);

    /// <summary>Gets the id of the password of an additional OOBE account.</summary>
    public static string OobeAccount(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return "oobe/account/" + accountId;
    }

    /// <summary>Gets the id of the password of a domain join account, keyed by its canonical account name.</summary>
    public static string DomainAccount(string canonicalAccount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAccount);
        return "domain/" + canonicalAccount;
    }

    /// <summary>Returns whether the id belongs to the sharing machinery and must never be exported or published.</summary>
    public static bool IsSharingInternal(string id) => id.StartsWith(SharingPrefix, StringComparison.Ordinal);

    private static string ForHash(string prefix, string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        return prefix + hash.ToLowerInvariant();
    }
}
