// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Secrets;

namespace Foundry.Core.Services.Sharing;

/// <summary>An export file opened with its passphrase, before it replaces the current configuration.</summary>
/// <param name="Name">Name the file was exported under (the exporting PC, or the legacy profile name).</param>
/// <param name="CreatedUtc">When the file was written; <see langword="default"/> for legacy files, which do not record it.</param>
/// <param name="IncludesPasswords">The file carries passwords.</param>
/// <param name="IsLegacy">The file is a retired <c>.foundryprofile</c> package or a legacy local profile.</param>
/// <param name="IsLegacyConnectionFile">The file is a legacy connection file: its synchronization key is ignored and the retired-sync notice applies.</param>
/// <param name="Payload">The decrypted export payload; legacy files are converted to the current shape. Never logged.</param>
public sealed record ImportPreview(string Name, DateTimeOffset CreatedUtc, bool IncludesPasswords, bool IsLegacy, bool IsLegacyConnectionFile, ConfigurationPackagePayload Payload);

/// <summary>The result of <see cref="ConfigurationTransfer.Apply"/>, ready to replace the current configuration and update the vault.</summary>
/// <param name="Configuration">The imported configuration with the local per-PC values and the library references of its imported files.</param>
/// <param name="VaultEntries">The passwords of <paramref name="Configuration"/>: imported ones over local ones. Sharing ids are never included.</param>
/// <param name="MissingContent">Content the configuration needs that is not available on this PC (packages, driver sets and images are never exported).</param>
public sealed record ImportOutcome(FoundryConfigurationDocument Configuration, IReadOnlyDictionary<string, string> VaultEntries, IReadOnlyList<PackageContent> MissingContent);

/// <summary>Encrypted export and import of the current configuration (spec 5.4 and 6.3).</summary>
/// <remarks>
/// Exports are unsigned: a wrong passphrase and a modified file both read as <see cref="PackageReadStatus.WrongPassword"/>.
/// Only <see cref="PackageKind.Export"/> payloads are imported; a publication manifest given as an export file is
/// <see cref="PackageReadStatus.Invalid"/>. Legacy <c>.foundryprofile</c> packages and connection files are recognized by their
/// magic and converted through <see cref="LegacyProfileReader"/> and <see cref="LegacyProfileMigration"/>.
/// </remarks>
public static class ConfigurationTransfer
{
    /// <summary>Extension of export files.</summary>
    public const string FileExtension = ".foundryconfig";

    /// <summary>Extension of the retired deployment profile exports, still accepted by import.</summary>
    public const string LegacyFileExtension = ".foundryprofile";

    private static readonly IReadOnlyDictionary<string, byte[]> NoFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> NoSecrets = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Writes an export of <paramref name="current"/> without its per-PC values: the referenced authoring files are embedded and
    /// the packages, driver sets and images are listed only. Passwords are included only when <paramref name="includePasswords"/>
    /// is set, and sharing ids never are.
    /// </summary>
    /// <remarks>
    /// The embedded files are leased while they are read. A file missing from the library or failing its hash check is not
    /// embedded; the import then lists it as missing content.
    /// </remarks>
    /// <exception cref="InvalidDataException">The embedded files exceed 16 MiB in total.</exception>
    /// <exception cref="ArgumentException">The passphrase does not satisfy <see cref="PassphrasePolicy"/>.</exception>
    public static byte[] Export(
        FoundryConfigurationDocument current,
        IReadOnlyDictionary<string, string> vault,
        AuthoringFileLibrary files,
        ReadOnlySpan<char> passphrase,
        bool includePasswords,
        string name)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(name);

        FoundryConfigurationDocument portable = PortableConfiguration.Strip(current);
        var payload = new ConfigurationPackagePayload
        {
            Kind = PackageKind.Export,
            CreatedUtc = DateTimeOffset.UtcNow,
            Name = name,
            Configuration = portable,
            Secrets = includePasswords
                ? vault.Where(entry => !SecretIds.IsSharingInternal(entry.Key)).ToDictionary(StringComparer.Ordinal)
                : null,
            Contents = PortableConfiguration.ListContents(portable),
            Files = ReadEmbeddedFiles(portable, files)
        };
        return ConfigurationPackageCodec.Write(payload, passphrase);
    }

    /// <summary>
    /// Opens an export file, or a legacy <c>.foundryprofile</c> package or connection file, with its passphrase. Never throws for
    /// malformed input.
    /// </summary>
    /// <returns>
    /// <see cref="PackageReadStatus.Ok"/> with a preview; <see cref="PackageReadStatus.WrongPassword"/> for a wrong passphrase or a
    /// damaged file; <see cref="PackageReadStatus.Unsupported"/> when a newer Foundry wrote it; otherwise
    /// <see cref="PackageReadStatus.Invalid"/>, including a publication manifest and a configuration carrying explicit nulls.
    /// </returns>
    public static PackageReadStatus TryPreview(ReadOnlySpan<byte> file, ReadOnlySpan<char> passphrase, out ImportPreview? preview)
    {
        preview = null;
        if (LegacyProfileReader.IsLegacyPackage(file))
        {
            return TryPreviewLegacy(file, passphrase, out preview);
        }

        PackageReadResult result = ConfigurationPackageCodec.Read(file, passphrase);
        if (result.Status != PackageReadStatus.Ok)
        {
            return result.Status is PackageReadStatus.Tampered or PackageReadStatus.InvalidSignature ? PackageReadStatus.Invalid : result.Status;
        }

        ConfigurationPackagePayload payload = result.Payload!;
        if (payload.Kind != PackageKind.Export || !IsWellFormedExport(payload))
        {
            return PackageReadStatus.Invalid;
        }

        preview = new ImportPreview(payload.Name ?? string.Empty, payload.CreatedUtc, payload.Secrets is not null, IsLegacy: false, IsLegacyConnectionFile: false, payload);
        return PackageReadStatus.Ok;
    }

    /// <summary>
    /// Converts a configuration read from a legacy profile (an exported package, a connection file or the local profile store)
    /// into an import preview: its files are embedded, its passwords mapped to vault ids, and its content listed.
    /// </summary>
    public static ImportPreview PreviewLegacy(LegacyConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        LegacyMigrationResult migrated = LegacyProfileMigration.Migrate(snapshot.Configuration, snapshot, activeProfileLocked: false, file =>
        {
            if (file.Content.Length > AuthoringFileLibrary.MaximumFileBytes) throw new InvalidDataException("AuthoringFile.TooLarge");
            string hash = Convert.ToHexStringLower(SHA256.HashData(file.Content));
            files[hash] = file.Content;
            return new AuthoringFileReference { ContentHash = hash, FileName = file.FileName, Length = file.Content.Length };
        });

        FoundryConfigurationDocument configuration = migrated.Configuration;
        var referenced = PortableConfiguration.FileReferences(configuration).Select(reference => reference.ContentHash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var payload = new ConfigurationPackagePayload
        {
            Kind = PackageKind.Export,
            Name = snapshot.DisplayName,
            Configuration = configuration,
            Secrets = migrated.VaultEntries.Count > 0 ? migrated.VaultEntries : null,
            Contents = PortableConfiguration.ListContents(configuration),
            Files = files.Where(file => referenced.Contains(file.Key)).ToDictionary(StringComparer.Ordinal)
        };
        return new ImportPreview(snapshot.DisplayName, default, payload.Secrets is not null, IsLegacy: true, snapshot.IsConnectionFile, payload);
    }

    /// <summary>Lists the content the previewed configuration needs that neither the file nor this PC provides.</summary>
    public static IReadOnlyList<PackageContent> ListMissingContent(
        ImportPreview preview,
        AuthoringFileLibrary files,
        Func<ContentPackageReference, bool> packageAvailable,
        Func<ContentPackageReference, bool> driverSetAvailable,
        Func<CustomImageReference, bool> imageAvailable)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(files);
        IReadOnlyDictionary<string, byte[]> embedded = preview.Payload.Files ?? NoFiles;
        return FindMissing(
            preview.Payload.Configuration,
            reference => embedded.ContainsKey(reference.ContentHash.ToLowerInvariant()) || files.IsAvailable(reference),
            packageAvailable,
            driverSetAvailable,
            imageAvailable);
    }

    /// <summary>
    /// Imports the embedded files into <paramref name="files"/>, keeps the per-PC values of <paramref name="local"/>, and merges
    /// the imported passwords over <paramref name="localVault"/>; the caller then replaces the configuration and writes the vault.
    /// </summary>
    /// <remarks>
    /// Only files the configuration references are imported. Local passwords are kept where the imported file has none, as long
    /// as their owner is in the imported configuration. Imported entries of sections without a <see cref="SecretMap"/> binding
    /// are kept as they are; sharing ids are dropped.
    /// </remarks>
    /// <exception cref="IOException">An embedded file cannot be written to the library.</exception>
    public static ImportOutcome Apply(
        ImportPreview preview,
        FoundryConfigurationDocument local,
        IReadOnlyDictionary<string, string> localVault,
        AuthoringFileLibrary files,
        Func<ContentPackageReference, bool> packageAvailable,
        Func<ContentPackageReference, bool> driverSetAvailable,
        Func<CustomImageReference, bool> imageAvailable)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(localVault);
        ArgumentNullException.ThrowIfNull(files);

        ConfigurationPackagePayload payload = preview.Payload;
        IReadOnlyDictionary<string, byte[]> embedded = payload.Files ?? NoFiles;
        var imported = new Dictionary<string, AuthoringFileReference>(StringComparer.Ordinal);
        FoundryConfigurationDocument configuration = PortableConfiguration.MapFileReferences(payload.Configuration, reference =>
        {
            string hash = reference.ContentHash.ToLowerInvariant();
            if (!imported.TryGetValue(hash, out AuthoringFileReference? stored))
            {
                if (!embedded.TryGetValue(hash, out byte[]? content)) return reference;
                stored = files.Import(content, reference.FileName);
                imported[hash] = stored;
            }

            return stored;
        });
        configuration = PortableConfiguration.ApplyLocal(configuration, local);
        return new ImportOutcome(
            configuration,
            MergeSecrets(configuration, payload.Secrets ?? NoSecrets, localVault),
            FindMissing(configuration, files.IsAvailable, packageAvailable, driverSetAvailable, imageAvailable));
    }

    private static PackageReadStatus TryPreviewLegacy(ReadOnlySpan<byte> file, ReadOnlySpan<char> passphrase, out ImportPreview? preview)
    {
        preview = null;
        try
        {
            preview = PreviewLegacy(LegacyProfileReader.ReadPackage(file, passphrase));
            return PackageReadStatus.Ok;
        }
        catch (LegacyPasswordException)
        {
            return PackageReadStatus.WrongPassword;
        }
        catch (InvalidDataException)
        {
            return PackageReadStatus.Invalid;
        }
    }

    /// <summary>Checks what the codec does not: every embedded file matches its hash and size, and no secret value is null.</summary>
    private static bool IsWellFormedExport(ConfigurationPackagePayload payload) =>
        (payload.Files ?? NoFiles).All(file => file.Value is { Length: <= AuthoringFileLibrary.MaximumFileBytes } content &&
            string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), file.Key, StringComparison.Ordinal)) &&
        (payload.Secrets ?? NoSecrets).All(secret => secret.Value is not null);

    private static Dictionary<string, byte[]> ReadEmbeddedFiles(FoundryConfigurationDocument configuration, AuthoringFileLibrary files)
    {
        AuthoringFileReference[] references = PortableConfiguration.FileReferences(configuration)
            .DistinctBy(reference => reference.ContentHash, StringComparer.OrdinalIgnoreCase).ToArray();
        var embedded = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        // The cleanup of unreferenced library files must not delete a file while it is read.
        using IDisposable lease = files.Lease(references.Select(reference => reference.ContentHash));
        foreach (AuthoringFileReference reference in references)
        {
            byte[] content;
            try
            {
                content = files.ReadVerified(reference);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }

            total += content.Length;
            if (total > ConfigurationPackageCodec.MaximumEmbeddedFileBytes) throw new InvalidDataException("ConfigurationTransfer.FilesTooLarge");
            embedded[reference.ContentHash.ToLowerInvariant()] = content;
        }

        return embedded;
    }

    private static Dictionary<string, string> MergeSecrets(
        FoundryConfigurationDocument configuration,
        IReadOnlyDictionary<string, string> incoming,
        IReadOnlyDictionary<string, string> localVault)
    {
        KeyValuePair<string, string>[] shared = incoming.Where(entry => !SecretIds.IsSharingInternal(entry.Key)).ToArray();
        var available = new Dictionary<string, string>(localVault, StringComparer.Ordinal);
        foreach ((string id, string value) in shared) available[id] = value;

        var entries = new Dictionary<string, string>(SecretMap.ToEntries(configuration, SecretMap.FromEntries(configuration, available)), StringComparer.Ordinal);
        foreach ((string id, string value) in shared.Where(entry => !SecretMap.IsOwned(entry.Key))) entries[id] = value;
        return entries;
    }

    private static PackageContent[] FindMissing(
        FoundryConfigurationDocument configuration,
        Func<AuthoringFileReference, bool> fileAvailable,
        Func<ContentPackageReference, bool> packageAvailable,
        Func<ContentPackageReference, bool> driverSetAvailable,
        Func<CustomImageReference, bool> imageAvailable)
    {
        ArgumentNullException.ThrowIfNull(packageAvailable);
        ArgumentNullException.ThrowIfNull(driverSetAvailable);
        ArgumentNullException.ThrowIfNull(imageAvailable);
        return PortableConfiguration.FileReferences(configuration).Where(reference => !fileAvailable(reference)).Select(PortableConfiguration.ToContent)
            .Concat(PortableConfiguration.UsedPackages(configuration).Where(package => !packageAvailable(package))
                .Select(package => PortableConfiguration.ToContent(package, SharedContentKind.Package)))
            .Concat(PortableConfiguration.UsedDriverSets(configuration).Where(package => !driverSetAvailable(package))
                .Select(package => PortableConfiguration.ToContent(package, SharedContentKind.DriverSet)))
            .Concat(PortableConfiguration.UsedImages(configuration).Where(image => !imageAvailable(image)).Select(PortableConfiguration.ToContent))
            .DistinctBy(content => (content.Kind, content.Hash.ToLowerInvariant()))
            .ToArray();
    }
}
