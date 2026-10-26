// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration.Legacy;

/// <summary>
/// A configuration recovered from a retired deployment profile: an exported <c>.foundryprofile</c>, a connection file or
/// a revision of the legacy local profile store.
/// </summary>
/// <param name="DisplayName">Name the profile had in the retired feature.</param>
/// <param name="Configuration">Settings migrated to the current authoring schema. Packages carry no per-PC paths; local revisions keep them.</param>
/// <param name="Secrets">Passwords the profile carried and that still match an entry of <paramref name="Configuration"/>.</param>
/// <param name="Files">Authoring files whose bytes the profile carried; driver sets and packages are never included.</param>
/// <param name="WasShared">The profile was enrolled in the retired folder synchronization (always true for a connection file).</param>
/// <param name="IsConnectionFile">The package is a legacy connection file; its synchronization key is ignored.</param>
public sealed record LegacyConfigurationSnapshot(
    string DisplayName,
    FoundryConfigurationDocument Configuration,
    IReadOnlyList<LegacySecret> Secrets,
    IReadOnlyList<LegacyFile> Files,
    bool WasShared,
    bool IsConnectionFile);

/// <summary>Families of passwords a legacy profile could carry.</summary>
public enum LegacySecretKind
{
    /// <summary>The deployment media protection password.</summary>
    DeploymentProtection,

    /// <summary>The personal Wi-Fi passphrase of the configured network.</summary>
    WifiPassphrase,

    /// <summary>The built-in OOBE administrator password.</summary>
    OobeAdministrator,

    /// <summary>The password of one additional OOBE account; the secret key is the account id.</summary>
    OobeAccount,

    /// <summary>The PFX password of the wired 802.1X certificate.</summary>
    WiredCertificatePassword,

    /// <summary>The PFX password of the Wi-Fi enterprise certificate.</summary>
    WifiCertificatePassword,

    /// <summary>The PFX password of the Autopilot hardware hash upload certificate.</summary>
    AutopilotCertificatePassword,

    /// <summary>The password of one domain join account; the secret key is the canonical account.</summary>
    DomainJoin
}

/// <summary>A password recovered from a legacy profile.</summary>
/// <param name="Kind">The password family.</param>
/// <param name="Key">The OOBE account id for <see cref="LegacySecretKind.OobeAccount"/>, the canonical account for <see cref="LegacySecretKind.DomainJoin"/>, otherwise null.</param>
/// <param name="Value">The password; empty when the profile recorded an intentionally blank password.</param>
public sealed record LegacySecret(LegacySecretKind Kind, string? Key, string Value);

/// <summary>The setting an authoring file recovered from a legacy profile belongs to.</summary>
public enum LegacyFileRole
{
    /// <summary>The wired 802.1X profile template.</summary>
    WiredProfile,

    /// <summary>The Wi-Fi enterprise profile template.</summary>
    WifiProfile,

    /// <summary>The wired 802.1X certificate.</summary>
    WiredCertificate,

    /// <summary>The Wi-Fi enterprise certificate.</summary>
    WifiCertificate,

    /// <summary>The Autopilot hardware hash upload certificate.</summary>
    AutopilotCertificate,

    /// <summary>A custom answer file, identified by its unattend id.</summary>
    Unattend
}

/// <summary>An authoring file recovered from a legacy profile.</summary>
/// <param name="Role">The setting the file belongs to.</param>
/// <param name="UnattendId">The answer-file id for <see cref="LegacyFileRole.Unattend"/>, otherwise null.</param>
/// <param name="FileName">A generated name whose extension comes from the legacy asset path.</param>
/// <param name="Content">The verified file bytes.</param>
public sealed record LegacyFile(LegacyFileRole Role, string? UnattendId, string FileName, byte[] Content);

/// <summary>A committed profile of the legacy local profile store.</summary>
/// <param name="LocalId">The local identity of the profile, which names its store directory.</param>
/// <param name="DisplayName">The name the profile had in the retired feature.</param>
/// <param name="IsActive">The profile was the active selection of the retired feature.</param>
/// <param name="IsShared">The profile was enrolled in the retired folder synchronization.</param>
public sealed record LegacyLocalProfile(Guid LocalId, string DisplayName, bool IsActive, bool IsShared);
