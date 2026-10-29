// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Secrets;

/// <summary>The configuration passwords held in memory, by field; <see cref="SecretMap"/> converts them to and from vault entries.</summary>
/// <remarks>A <see langword="null"/> value means no password is known. The record never enters the configuration document.</remarks>
public sealed record SecretSnapshot
{
    /// <summary>Gets the confirmed deployment media protection password.</summary>
    public string? DeploymentProtection { get; init; }

    /// <summary>Gets the personal Wi-Fi passphrase.</summary>
    public string? WifiPassphrase { get; init; }

    /// <summary>Gets the PFX password of the wired 802.1X certificate.</summary>
    public string? Dot1xCertificatePassword { get; init; }

    /// <summary>Gets the PFX password of the Wi-Fi enterprise certificate.</summary>
    public string? WifiCertificatePassword { get; init; }

    /// <summary>Gets the PFX password of the Autopilot boot media certificate.</summary>
    public string? AutopilotCertificatePassword { get; init; }

    /// <summary>Gets the confirmed password of the OOBE administrator account.</summary>
    public string? OobeAdministrator { get; init; }

    /// <summary>Gets the confirmed passwords of the additional OOBE accounts, keyed by account id.</summary>
    public IReadOnlyDictionary<string, string> OobeAccounts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the domain join passwords, keyed by account name in any spelling; lookups use the canonical account name.</summary>
    public IReadOnlyDictionary<string, string> DomainAccounts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the network cache account passwords, keyed by account id.</summary>
    public IReadOnlyDictionary<string, string> NetworkCacheAccounts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
