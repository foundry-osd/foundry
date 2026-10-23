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
    DeploymentProtection,
    WifiPassphrase,
    OobeAdministrator,
    OobeAccount,
    WiredCertificatePassword,
    WifiCertificatePassword,
    AutopilotCertificatePassword,
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
    WiredProfile,
    WifiProfile,
    WiredCertificate,
    WifiCertificate,
    AutopilotCertificate,
    Unattend
}

/// <summary>An authoring file recovered from a legacy profile.</summary>
/// <param name="Role">The setting the file belongs to.</param>
/// <param name="UnattendId">The answer-file id for <see cref="LegacyFileRole.Unattend"/>, otherwise null.</param>
/// <param name="FileName">A generated name whose extension comes from the legacy asset path.</param>
/// <param name="Content">The verified file bytes.</param>
public sealed record LegacyFile(LegacyFileRole Role, string? UnattendId, string FileName, byte[] Content);

/// <summary>A committed profile of the legacy local profile store.</summary>
public sealed record LegacyLocalProfile(Guid LocalId, string DisplayName, bool IsActive, bool IsShared);

/// <summary>The passphrase does not open the legacy package.</summary>
public sealed class LegacyPasswordException() : Exception("The passphrase does not open this legacy profile.");

/// <summary>The Credential Manager key of a legacy local profile is unavailable for the current Windows user.</summary>
public sealed class LegacyProfileLockedException(Guid localId) : IOException("The legacy local profile key is unavailable for this Windows user.")
{
    public Guid LocalId { get; } = localId;
}
