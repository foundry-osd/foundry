// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Secrets;
using Foundry.Services.Settings;
using Foundry.Utilities.Security;
using Serilog;

namespace Foundry.Services.Configuration;

/// <summary>
/// Runs the first-launch migration from the removed deployment profiles once: the active legacy profile gives its passwords
/// and file bytes, the current configuration keeps its settings.
/// </summary>
/// <remarks>
/// Runs at startup after the configuration state loads and before the secret vault bridge restores passwords. The legacy profile
/// store and its Credential Manager entries are never modified; only the decrypted working copies in its <c>Staging</c> folder are
/// deleted. Completion and the notices to show are recorded in the application settings. A configuration that cannot be saved
/// leaves the migration incomplete so the next launch retries it. Secret values and file contents are never logged.
/// </remarks>
internal sealed class LegacyMigrationRunner(
    IAppSettingsService settings,
    IFoundryConfigurationStateService configuration,
    AuthoringFileLibrary files,
    LocalSecretVault vault,
    IWindowsCredentialStore credentials,
    ILogger logger)
{
    private readonly ILogger logger = logger.ForContext<LegacyMigrationRunner>();

    /// <summary>Migrates once; does nothing when the migration already completed. A failure never blocks startup.</summary>
    public void Run()
    {
        MigrationSettings migrations = settings.Current.Migrations;
        if (migrations.ConfigurationSharingCompleted) return;

        logger.Information("Legacy configuration migration started.");
        try
        {
            Migrate(migrations);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Legacy configuration migration failed and will be retried at the next launch.");
        }
    }

    private void Migrate(MigrationSettings migrations)
    {
        (LegacyConfigurationSnapshot? profile, bool locked, bool shared) = ReadActiveProfile();
        int importedFiles = 0;
        LegacyMigrationResult result = LegacyProfileMigration.Migrate(configuration.Current, profile, locked, file =>
        {
            AuthoringFileReference reference = files.Import(file.Content, file.FileName);
            importedFiles++;
            return reference;
        });

        try
        {
            if (!ReferenceEquals(result.Configuration, configuration.Current))
            {
                configuration.Replace(result.Configuration, static () => { });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Legacy configuration migration could not save the configuration and will be retried at the next launch.");
            return;
        }

        var notices = new HashSet<MigrationNotice>(result.Notices);
        if (locked && shared) notices.Add(MigrationNotice.LegacySyncRetired);
        if (!SaveVaultEntries(result.VaultEntries)) notices.Add(MigrationNotice.PasswordsNeeded);
        DeleteStaging();

        migrations.ConfigurationSharingCompleted = true;
        migrations.PendingNotices = notices.Order().ToList();
        try
        {
            settings.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The settings service logged the failure; the migration result is already in the configuration and the vault.
            logger.Warning(exception, "Legacy configuration migration completed but its completion could not be recorded.");
        }

        logger.Information(
            "Legacy configuration migration completed. ProfileFound={ProfileFound}, ProfileLocked={ProfileLocked}, ImportedFileCount={ImportedFileCount}, SecretCount={SecretCount}, Notices={Notices}",
            profile is not null, locked, importedFiles, result.VaultEntries.Count, string.Join(",", migrations.PendingNotices));
    }

    private (LegacyConfigurationSnapshot? Profile, bool Locked, bool Shared) ReadActiveProfile()
    {
        string root = Constants.LegacyProfilesDirectoryPath;
        LegacyLocalProfile? active;
        try
        {
            active = LegacyProfileReader.ListLocalProfiles(root).FirstOrDefault(profile => profile.IsActive);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Legacy profile store could not be listed; settings are kept and passwords must be entered again.");
            return (null, true, false);
        }

        if (active is null) return (null, false, false);
        try
        {
            LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadLocalProfile(root, active.LocalId, credentials);
            return (snapshot, false, snapshot.WasShared);
        }
        catch (LegacyProfileLockedException)
        {
            logger.Warning("Active legacy profile is locked; settings are kept and passwords must be entered again.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.Warning(exception, "Active legacy profile could not be read; settings are kept and passwords must be entered again.");
        }

        return (null, true, active.IsShared);
    }

    /// <summary>Adds the recovered passwords to the vault without replacing a value it already holds.</summary>
    /// <returns>False when the passwords could not be saved and must be entered again.</returns>
    private bool SaveVaultEntries(IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count == 0) return true;
        try
        {
            IReadOnlyDictionary<string, string> existing = vault.Load(out VaultStatus status);
            var merged = new Dictionary<string, string>(status == VaultStatus.Ok ? existing : new Dictionary<string, string>(), StringComparer.Ordinal);
            foreach ((string id, string value) in entries) merged.TryAdd(id, value);
            vault.Save(merged);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.Warning(exception, "Recovered legacy passwords could not be saved to the secret vault. SecretCount={SecretCount}", entries.Count);
            return false;
        }
    }

    private void DeleteStaging()
    {
        try
        {
            if (LegacyProfileMigration.DeleteStaging(Constants.LegacyProfilesDirectoryPath))
            {
                logger.Information("Legacy profile staging copies removed.");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Legacy profile staging copies could not be removed.");
        }
    }
}
