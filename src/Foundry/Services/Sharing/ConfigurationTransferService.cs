// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Configuration;
using Foundry.Services.Secrets;
using Foundry.Utilities.Security;
using Serilog;

namespace Foundry.Services.Sharing;

/// <summary>Outcome of opening a configuration to import.</summary>
public enum ImportPreviewStatus
{
    /// <summary>The configuration was opened; the preview is set.</summary>
    Ok,

    /// <summary>The passphrase is wrong or the file was modified: an export is unsigned, so the two cannot be told apart.</summary>
    WrongPassphrase,

    /// <summary>The file is not a Foundry configuration export, or it is damaged.</summary>
    Invalid,

    /// <summary>The file was written by a newer Foundry version.</summary>
    Unsupported,

    /// <summary>The legacy local profile cannot be opened by this Windows user: its key is missing.</summary>
    Locked,

    /// <summary>The file or profile could not be read from disk.</summary>
    Unreadable
}

/// <summary>A configuration opened for import, with the content this PC lacks.</summary>
/// <param name="Status">Whether the configuration could be opened.</param>
/// <param name="Preview">The opened configuration; set only when <paramref name="Status"/> is <see cref="ImportPreviewStatus.Ok"/>.</param>
/// <param name="MissingContent">Content neither the file nor this PC provides; packages, driver sets and images must be imported again.</param>
public sealed record ConfigurationImportPreview(ImportPreviewStatus Status, ImportPreview? Preview, IReadOnlyList<PackageContent> MissingContent);

/// <summary>The result of a completed import.</summary>
/// <param name="MissingContent">Content the imported configuration needs that is not on this PC.</param>
/// <param name="PasswordsSaved">False when the imported passwords could not be written to the vault and must be entered again.</param>
public sealed record ConfigurationImportResult(IReadOnlyList<PackageContent> MissingContent, bool PasswordsSaved);

/// <summary>
/// Exports the current configuration to an encrypted file and imports one in its place, through the configuration state service
/// and the secret vault bridge, the single vault writer.
/// </summary>
/// <remarks>
/// Call members on the UI thread; encryption and file access run in the background. An import replaces the configuration under
/// <see cref="ConfigurationReplacementGate"/>, so a running content cleanup cannot delete what the imported configuration
/// references, and leases its files until the configuration that references them is current. Passphrases, passwords and file
/// contents are never logged.
/// </remarks>
public sealed class ConfigurationTransferService(
    IFoundryConfigurationStateService configuration,
    ISecretVaultBridge vault,
    AuthoringFileLibrary files,
    ContentPackageLibraryService postInstallationLibrary,
    CustomDriverSetLibrary driverSetLibrary,
    CustomImageLibraryService imageLibrary,
    ConfigurationReplacementGate replacementGate,
    IWindowsCredentialStore credentials,
    ILogger logger)
{
    /// <summary>Largest file import reads; larger files are not configuration exports.</summary>
    private const long MaximumImportFileBytes = 64 * 1024 * 1024;

    private readonly ILogger logger = logger.ForContext<ConfigurationTransferService>();

    /// <summary>Writes the current configuration, without its per-PC values, to <paramref name="path"/>.</summary>
    /// <param name="path">Destination file; replaced atomically.</param>
    /// <param name="passphrase">Passphrase satisfying <see cref="PassphrasePolicy"/>.</param>
    /// <param name="includePasswords">Includes the remembered passwords.</param>
    /// <returns>The number of referenced files that were missing or damaged in the library and could not be included.</returns>
    /// <exception cref="InvalidDataException">The embedded files exceed the export limit.</exception>
    /// <exception cref="IOException">The file cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the destination is denied.</exception>
    public async Task<int> ExportAsync(string path, string passphrase, bool includePasswords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(passphrase);
        FoundryConfigurationDocument current = configuration.Current;
        IReadOnlyDictionary<string, string> entries = includePasswords ? vault.ReadEntries() : new Dictionary<string, string>();
        if (includePasswords && vault.Status == VaultStatus.Unreadable)
        {
            logger.Warning("Secret vault is unreadable; the export carries only the passwords saved since.");
        }

        logger.Information("Configuration export started. IncludePasswords={IncludePasswords}", includePasswords);
        try
        {
            ConfigurationExport export = await Task.Run(() =>
            {
                ConfigurationExport written = ConfigurationTransfer.Export(current, entries, files, passphrase, includePasswords, Environment.MachineName);
                WriteAtomically(path, written.Package);
                return written;
            });
            if (export.SkippedFileCount > 0)
            {
                logger.Warning(
                    "Configuration exported without the referenced files that are missing or damaged in the library. SkippedFileCount={SkippedFileCount}",
                    export.SkippedFileCount);
            }

            logger.Information(
                "Configuration export completed. IncludePasswords={IncludePasswords}, Length={Length}, SkippedFileCount={SkippedFileCount}",
                includePasswords, export.Package.Length, export.SkippedFileCount);
            return export.SkippedFileCount;
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Configuration export failed. IncludePasswords={IncludePasswords}", includePasswords);
            throw;
        }
    }

    /// <summary>Opens an export file, or a legacy <c>.foundryprofile</c> package or connection file, with its passphrase.</summary>
    public async Task<ConfigurationImportPreview> PreviewFileAsync(string path, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(passphrase);
        ConfigurationImportPreview result = await Task.Run(() =>
        {
            byte[] bytes;
            try
            {
                if (new FileInfo(path).Length > MaximumImportFileBytes) return Failed(ImportPreviewStatus.Invalid);
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.Warning(exception, "Configuration file could not be read for import.");
                return Failed(ImportPreviewStatus.Unreadable);
            }

            PackageReadStatus status = ConfigurationTransfer.TryPreview(bytes, passphrase, out ImportPreview? preview);
            return preview is null ? Failed(Map(status)) : Opened(preview);
        });
        LogPreview(result);
        return result;
    }

    /// <summary>Lists the profiles of the legacy profile store of this Windows user; an unreadable store lists none.</summary>
    public async Task<IReadOnlyList<LegacyLocalProfile>> ListLegacyProfilesAsync()
    {
        try
        {
            return await Task.Run(() => LegacyProfileReader.ListLocalProfiles(Constants.LegacyProfilesDirectoryPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning(exception, "Legacy profile store could not be listed for import.");
            return [];
        }
    }

    /// <summary>Opens a profile of the legacy profile store of this Windows user.</summary>
    public async Task<ConfigurationImportPreview> PreviewLegacyProfileAsync(Guid localId)
    {
        ConfigurationImportPreview result = await Task.Run(() =>
        {
            try
            {
                LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadLocalProfile(Constants.LegacyProfilesDirectoryPath, localId, credentials);
                return Opened(ConfigurationTransfer.PreviewLegacy(snapshot));
            }
            catch (LegacyProfileLockedException)
            {
                return Failed(ImportPreviewStatus.Locked);
            }
            catch (InvalidDataException)
            {
                return Failed(ImportPreviewStatus.Invalid);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
            {
                logger.Warning(exception, "Legacy profile could not be read for import.");
                return Failed(ImportPreviewStatus.Unreadable);
            }
        });
        LogPreview(result);
        return result;
    }

    /// <summary>
    /// Replaces the current configuration with <paramref name="preview"/>: its files go to the library, its passwords to the vault,
    /// and the per-PC values of this PC are kept.
    /// </summary>
    /// <exception cref="IOException">A file or the configuration cannot be saved; the current configuration is then unchanged.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the library or the configuration is denied.</exception>
    public async Task<ConfigurationImportResult> ImportAsync(ImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        logger.Information(
            "Configuration import started. IsLegacy={IsLegacy}, IsLegacyConnectionFile={IsLegacyConnectionFile}, IncludesPasswords={IncludesPasswords}, FileCount={FileCount}",
            preview.IsLegacy, preview.IsLegacyConnectionFile, preview.IncludesPasswords, preview.Payload.Files?.Count ?? 0);
        ImportOutcome outcome;
        try
        {
            // Files imported for the new configuration stay leased until it is current, so the authoring file cleanup keeps them.
            using IDisposable lease = files.Lease(preview.Payload.Files?.Keys ?? []);
            using IDisposable gate = await replacementGate.EnterAsync();
            FoundryConfigurationDocument local = configuration.Current;
            IReadOnlyDictionary<string, string> localVault = vault.ReadEntries();
            outcome = await Task.Run(() => ConfigurationTransfer.Apply(
                preview, local, localVault, files, postInstallationLibrary.IsAvailable, driverSetLibrary.IsAvailable, imageLibrary.IsAvailable));
            configuration.Replace(outcome.Configuration, static () => { });
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Configuration import failed; the current configuration is unchanged.");
            throw;
        }

        bool passwordsSaved = SavePasswords(outcome.VaultEntries);
        await configuration.RefreshCustomDriversReadinessAsync();
        logger.Information(
            "Configuration import completed. SecretCount={SecretCount}, PasswordsSaved={PasswordsSaved}, MissingContentCount={MissingContentCount}",
            outcome.VaultEntries.Count, passwordsSaved, outcome.MissingContent.Count);
        return new ConfigurationImportResult(outcome.MissingContent, passwordsSaved);
    }

    private bool SavePasswords(IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count == 0) return true;
        try
        {
            vault.WriteEntries(entries);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.Warning(exception, "Imported passwords could not be saved to the secret vault. SecretCount={SecretCount}", entries.Count);
            return false;
        }
    }

    private ConfigurationImportPreview Opened(ImportPreview preview) =>
        new(ImportPreviewStatus.Ok, preview, ConfigurationTransfer.ListMissingContent(
            preview, files, postInstallationLibrary.IsAvailable, driverSetLibrary.IsAvailable, imageLibrary.IsAvailable));

    private static ConfigurationImportPreview Failed(ImportPreviewStatus status) => new(status, null, []);

    private static ImportPreviewStatus Map(PackageReadStatus status) => status switch
    {
        PackageReadStatus.WrongPassword => ImportPreviewStatus.WrongPassphrase,
        PackageReadStatus.Unsupported => ImportPreviewStatus.Unsupported,
        _ => ImportPreviewStatus.Invalid
    };

    private void LogPreview(ConfigurationImportPreview result)
    {
        if (result.Preview is { } preview)
        {
            logger.Information(
                "Configuration opened for import. IsLegacy={IsLegacy}, IncludesPasswords={IncludesPasswords}, MissingContentCount={MissingContentCount}",
                preview.IsLegacy, preview.IncludesPasswords, result.MissingContent.Count);
        }
        else
        {
            logger.Information("Configuration could not be opened for import. Status={Status}", result.Status);
        }
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        string fullPath = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
