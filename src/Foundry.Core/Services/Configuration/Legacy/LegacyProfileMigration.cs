// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Secrets;

namespace Foundry.Core.Services.Configuration.Legacy;

/// <summary>One-time notices the first launch after the legacy migration shows.</summary>
/// <remarks>Pending notices are persisted by name in the per-user <see cref="LegacyMigrationStateFile"/> until dismissed; never rename them.</remarks>
public enum MigrationNotice
{
    /// <summary>The active profile was locked or unreadable, or some of its passwords no longer match: they must be entered again.</summary>
    PasswordsNeeded = 0,

    /// <summary>The active profile was enrolled in the retired folder synchronization, which is no longer supported.</summary>
    LegacySyncRetired = 1,

    /// <summary>A file the configuration needs could not be recovered and must be selected again.</summary>
    FilesToReselect = 2,
}

/// <summary>The outcome of <see cref="LegacyProfileMigration.Migrate"/>.</summary>
/// <param name="Configuration">The configuration to keep; the same instance as the input when nothing changed.</param>
/// <param name="VaultEntries">The recovered passwords keyed by vault id.</param>
/// <param name="Notices">The notices to show once.</param>
public sealed record LegacyMigrationResult(
    FoundryConfigurationDocument Configuration,
    IReadOnlyDictionary<string, string> VaultEntries,
    IReadOnlySet<MigrationNotice> Notices);

/// <summary>Moves the passwords and file bytes of the active legacy profile into the current configuration and the vault.</summary>
/// <remarks>
/// Settings always come from the current configuration, which holds the most recent edits. Each file the profile carried replaces
/// the reference of its role, because profile bytes take precedence over a file re-imported from a legacy host path; an answer
/// file is accepted only when its bytes match the validated digest of the entry with the same id. Passwords are matched by
/// context against the result: the Wi-Fi passphrase only while the SSID is unchanged, a certificate password only for the
/// certificate the profile carried (keyed by its new hash), and account passwords only for accounts the configuration still lists;
/// a password dropped because its context changed raises <see cref="MigrationNotice.PasswordsNeeded"/>.
/// The legacy store itself is never modified.
/// </remarks>
public static class LegacyProfileMigration
{
    private const string StagingDirectoryName = "Staging";

    /// <summary>Computes the migrated configuration, vault entries and notices; files are imported through <paramref name="importFile"/>.</summary>
    /// <param name="current">The configuration loaded from <c>foundry.config.json</c>.</param>
    /// <param name="activeProfile">The active legacy profile, or <see langword="null"/> when there is none or it could not be read.</param>
    /// <param name="activeProfileLocked">The active profile exists but its key is missing or its content is unreadable.</param>
    /// <param name="importFile">Stores a recovered file in the authoring file library. A file that fails to import is skipped.</param>
    public static LegacyMigrationResult Migrate(
        FoundryConfigurationDocument current,
        LegacyConfigurationSnapshot? activeProfile,
        bool activeProfileLocked,
        Func<LegacyFile, AuthoringFileReference> importFile)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(importFile);

        var notices = new HashSet<MigrationNotice>();
        if (activeProfileLocked) notices.Add(MigrationNotice.PasswordsNeeded);

        FoundryConfigurationDocument configuration = current;
        IReadOnlyDictionary<string, string> entries = new Dictionary<string, string>(StringComparer.Ordinal);
        if (activeProfile is not null)
        {
            Dictionary<string, AuthoringFileReference> imported = ImportFiles(activeProfile.Files, importFile);
            configuration = imported.Count == 0 ? current : ApplyFiles(current, imported);
            entries = SecretMap.ToEntries(configuration, MapSecrets(activeProfile, configuration, imported));
            // A password that no longer matches its context (another SSID, certificate or account) must be entered again.
            if (entries.Count < CountUsablePasswords(activeProfile.Secrets)) notices.Add(MigrationNotice.PasswordsNeeded);
            if (activeProfile.WasShared) notices.Add(MigrationNotice.LegacySyncRetired);
        }

        if (HasMissingRequiredFile(configuration)) notices.Add(MigrationNotice.FilesToReselect);
        return new LegacyMigrationResult(configuration, entries, notices);
    }

    /// <summary>
    /// Deletes the <c>Staging</c> folder of the legacy profile store, where the retired feature left decrypted working copies of
    /// certificates and answer files. The profile store itself (heads, revisions) and Credential Manager are not touched.
    /// </summary>
    /// <remarks>A redirected (reparse point) staging folder is unlinked without following it. A missing folder is not an error.</remarks>
    /// <returns>Whether a staging folder existed and was removed.</returns>
    /// <exception cref="IOException">A staged file or folder cannot be deleted.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to a staged file or folder is denied.</exception>
    public static bool DeleteStaging(string legacyProfilesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyProfilesRoot);
        var staging = new DirectoryInfo(Path.Combine(Path.GetFullPath(legacyProfilesRoot), StagingDirectoryName));
        if (!staging.Exists) return false;
        if (staging.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            staging.Delete(recursive: false);
            return true;
        }

        // Read-only working copies would make the recursive delete fail; redirected children are unlinked, never followed.
        foreach (FileInfo file in EnumerateFiles(staging)) file.Attributes = FileAttributes.Normal;
        staging.Delete(recursive: true);
        return true;
    }

    private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo directory)
    {
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            if (entry is FileInfo file && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                yield return file;
            }
            else if (entry is DirectoryInfo child && !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (FileInfo nested in EnumerateFiles(child)) yield return nested;
            }
        }
    }

    private static Dictionary<string, AuthoringFileReference> ImportFiles(IReadOnlyList<LegacyFile> files, Func<LegacyFile, AuthoringFileReference> importFile)
    {
        var imported = new Dictionary<string, AuthoringFileReference>(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyFile file in files)
        {
            string key = Key(file.Role, file.UnattendId);
            if (imported.ContainsKey(key)) continue;
            try
            {
                imported[key] = importFile(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // The reference keeps its current value; a still-missing required file raises FilesToReselect.
            }
        }
        return imported;
    }

    private static FoundryConfigurationDocument ApplyFiles(FoundryConfigurationDocument current, Dictionary<string, AuthoringFileReference> imported)
    {
        NetworkSettings network = current.Network;
        AutopilotHardwareHashUploadSettings hardwareHash = current.Autopilot.HardwareHashUpload;
        return current with
        {
            Network = network with
            {
                Dot1x = ReplaceCertificate(
                    network.Dot1x with { ProfileTemplate = Find(LegacyFileRole.WiredProfile) ?? network.Dot1x.ProfileTemplate },
                    Find(LegacyFileRole.WiredCertificate)),
                Wifi = ReplaceCertificate(
                    network.Wifi with { EnterpriseProfileTemplate = Find(LegacyFileRole.WifiProfile) ?? network.Wifi.EnterpriseProfileTemplate },
                    Find(LegacyFileRole.WifiCertificate))
            },
            Autopilot = current.Autopilot with
            {
                HardwareHashUpload = hardwareHash with
                {
                    BootMediaCertificate = hardwareHash.BootMediaCertificate with
                    {
                        Pfx = Find(LegacyFileRole.AutopilotCertificate) ?? hardwareHash.BootMediaCertificate.Pfx
                    }
                }
            },
            Unattend = current.Unattend with
            {
                Files = current.Unattend.Files.Select(file =>
                    Find(LegacyFileRole.Unattend, file.Id) is { } answerFile &&
                    string.Equals(answerFile.ContentHash, file.ContentHash, StringComparison.OrdinalIgnoreCase)
                        ? file with { File = answerFile }
                        : file).ToArray()
            }
        };

        AuthoringFileReference? Find(LegacyFileRole role, string? unattendId = null) =>
            imported.GetValueOrDefault(Key(role, unattendId));
    }

    /// <summary>
    /// A legacy profile held one certificate per feature: its file replaces the certificate the JSON carried (imported from a
    /// host path) and takes the slot of its shape, PFX as client certificate, any other file as the single trusted root.
    /// </summary>
    private static Dot1xSettings ReplaceCertificate(Dot1xSettings settings, AuthoringFileReference? found) => found is null
        ? settings
        : NetworkCertificateShapes.WithCertificate(settings with { ClientCertificate = null, TrustedRootCertificates = [] }, found);

    /// <inheritdoc cref="ReplaceCertificate(Dot1xSettings, AuthoringFileReference?)"/>
    private static WifiSettings ReplaceCertificate(WifiSettings settings, AuthoringFileReference? found) => found is null
        ? settings
        : NetworkCertificateShapes.WithCertificate(settings with { ClientCertificate = null, TrustedRootCertificates = [] }, found);

    private static SecretSnapshot MapSecrets(
        LegacyConfigurationSnapshot profile,
        FoundryConfigurationDocument configuration,
        Dictionary<string, AuthoringFileReference> imported)
    {
        bool sameWifi = string.Equals(profile.Configuration.Network.Wifi.Ssid, configuration.Network.Wifi.Ssid, StringComparison.Ordinal);
        return new SecretSnapshot
        {
            DeploymentProtection = Password(LegacySecretKind.DeploymentProtection),
            WifiPassphrase = sameWifi ? Password(LegacySecretKind.WifiPassphrase) : null,
            Dot1xCertificatePassword = CertificatePassword(LegacySecretKind.WiredCertificatePassword, LegacyFileRole.WiredCertificate),
            WifiCertificatePassword = CertificatePassword(LegacySecretKind.WifiCertificatePassword, LegacyFileRole.WifiCertificate),
            AutopilotCertificatePassword = CertificatePassword(LegacySecretKind.AutopilotCertificatePassword, LegacyFileRole.AutopilotCertificate),
            OobeAdministrator = Password(LegacySecretKind.OobeAdministrator),
            OobeAccounts = Keyed(LegacySecretKind.OobeAccount),
            DomainAccounts = Keyed(LegacySecretKind.DomainJoin)
        };

        LegacySecret? Find(LegacySecretKind kind) => profile.Secrets.FirstOrDefault(secret => secret.Kind == kind);

        // A blank account or protection password is not a usable password; a PFX password may be blank.
        string? Password(LegacySecretKind kind) => Find(kind)?.Value is { Length: > 0 } value ? value : null;

        // The legacy password belongs to the certificate the profile carried, so it follows only that file.
        string? CertificatePassword(LegacySecretKind kind, LegacyFileRole role) =>
            imported.ContainsKey(Key(role, null)) ? Find(kind)?.Value : null;

        Dictionary<string, string> Keyed(LegacySecretKind kind) => profile.Secrets
            .Where(secret => secret.Kind == kind && !string.IsNullOrWhiteSpace(secret.Key) && secret.Value.Length > 0)
            .DistinctBy(secret => secret.Key, StringComparer.Ordinal)
            .ToDictionary(secret => secret.Key!, secret => secret.Value, StringComparer.Ordinal);
    }

    /// <summary>Counts the recovered passwords that would each map to one vault id when their context still matches.</summary>
    private static int CountUsablePasswords(IReadOnlyList<LegacySecret> secrets) => secrets
        .Where(secret => secret.Kind switch
        {
            LegacySecretKind.OobeAccount or LegacySecretKind.DomainJoin => !string.IsNullOrWhiteSpace(secret.Key) && secret.Value.Length > 0,
            LegacySecretKind.WiredCertificatePassword or LegacySecretKind.WifiCertificatePassword or LegacySecretKind.AutopilotCertificatePassword => true,
            _ => secret.Value.Length > 0
        })
        .DistinctBy(secret => (secret.Kind, secret.Key))
        .Count();

    /// <summary>Applies the media build rules: a reference an enabled feature needs is still null.</summary>
    private static bool HasMissingRequiredFile(FoundryConfigurationDocument configuration)
    {
        NetworkSettings network = configuration.Network;
        bool wired = network.Dot1x.IsEnabled;
        bool wifi = network.WifiProvisioned && network.Wifi.IsEnabled;
        return (wired && network.Dot1x.ProfileTemplate is null) ||
            (wired && network.Dot1x.RequiresCertificate && !NetworkCertificateShapes.HasCertificates(network.Dot1x)) ||
            (wifi && network.Wifi.HasEnterpriseProfile && network.Wifi.EnterpriseProfileTemplate is null) ||
            (wifi && network.Wifi.RequiresCertificate && !NetworkCertificateShapes.HasCertificates(network.Wifi)) ||
            (configuration.Autopilot is { IsEnabled: true, ProvisioningMode: AutopilotProvisioningMode.HardwareHashUpload } &&
                configuration.Autopilot.HardwareHashUpload.BootMediaCertificate.Pfx is null) ||
            (configuration.Unattend.IsEnabled && configuration.Unattend.Files.Any(file => file.File is null));
    }

    private static string Key(LegacyFileRole role, string? unattendId) => role == LegacyFileRole.Unattend ? $"{role}/{unattendId}" : role.ToString();
}
