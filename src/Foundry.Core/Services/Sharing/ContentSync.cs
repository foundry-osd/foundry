// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Utilities.IO;
using Serilog;

namespace Foundry.Core.Services.Sharing;

/// <summary>Result of <see cref="ContentSync.FetchAsync"/>.</summary>
/// <param name="Fetched">Content items this fetch imported; a hash listed under two kinds counts once per kind.</param>
/// <param name="Failed">
/// Content that decrypted but breaks the target library's rules or does not match its hash (for example a driver set without an INF
/// file), whose import failed unexpectedly, or that its library still lacks after the import (an inconsistent configuration reference).
/// It is never counted as fetched; it fails the same way until the reference publishes it again.
/// </param>
/// <param name="NotYetAvailable">
/// Content whose file is missing, incomplete or fails authentication (a cloud folder that has not delivered it yet, or a "Reset
/// publication" rewriting it in place), that the temporary volume has no room to decrypt, or that a local I/O failure kept out of its
/// library. Retry at the next check.
/// </param>
public sealed record ContentSyncResult(int Fetched, IReadOnlyList<PackageContent> Failed, IReadOnlyList<PackageContent> NotYetAvailable);

/// <summary>
/// Fetches the content a publication lists into the local libraries of a follower. Each content file is decrypted
/// into an owner-only temporary file (<see cref="PrivateDirectory"/>), deleted on every path, and checked before anything reaches a
/// library: files by their SHA-256, packages and driver sets by <see cref="ContentPackageArchive.ImportAsync"/> (manifest hash, every
/// file, the library kind rules), images by <see cref="CustomImageLibraryService.ImportFromPublicationAsync"/> (WIM length, hash and
/// index numbers). The plaintext layouts are those of <see cref="LocalContentSource"/>.
/// </summary>
/// <remarks>
/// Only content the configuration references and the local library lacks is fetched (<see cref="Missing"/>), so a fetch interrupted by
/// cancellation, a crash or content that has not arrived resumes with the content still missing and never downloads an imported item
/// again. A partly decrypted item is not resumed: it is decrypted again from its first chunk. A hash listed under both
/// <see cref="SharedContentKind.Package"/> and <see cref="SharedContentKind.DriverSet"/> is decrypted once and imported into each
/// library.
/// </remarks>
public sealed class ContentSync
{
    private const int MasterKeySize = 32;
    private const int BufferSize = 128 * 1024;
    private const int ProgressLogSteps = 4;

    /// <summary>Free space kept on the temporary volume beyond the decrypted content.</summary>
    private const long TemporarySpaceMargin = 64L * 1024 * 1024;

    private readonly AuthoringFileLibrary files;
    private readonly ContentPackageLibraryService packages;
    private readonly CustomDriverSetLibrary driverSets;
    private readonly CustomImageLibraryService images;
    private readonly string temporaryRoot;

    /// <summary>Creates a fetcher into the four libraries of this PC.</summary>
    /// <param name="files">Authoring file library.</param>
    /// <param name="packages">Post-installation package library.</param>
    /// <param name="driverSets">Custom driver set library.</param>
    /// <param name="images">Custom image library.</param>
    /// <param name="temporaryRoot">
    /// Dedicated root of the owner-only directories that hold decrypted content until it is imported; defaults to
    /// <c>%TEMP%\Foundry\ContentSync</c>.
    /// </param>
    public ContentSync(AuthoringFileLibrary files, ContentPackageLibraryService packages, CustomDriverSetLibrary driverSets,
        CustomImageLibraryService images, string? temporaryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(driverSets);
        ArgumentNullException.ThrowIfNull(images);
        this.files = files;
        this.packages = packages;
        this.driverSets = driverSets;
        this.images = images;
        this.temporaryRoot = temporaryRoot ?? Path.Combine(Path.GetTempPath(), "Foundry", "ContentSync");
    }

    /// <summary>
    /// Lists the content of <paramref name="manifest"/> that the local libraries lack, by the lightweight availability checks of each
    /// library against the references of the manifest configuration with the same hash (every reference of a file, since one hash can
    /// be stored under several names). Content that no reference of the configuration uses is not needed and never listed.
    /// </summary>
    public IReadOnlyList<PackageContent> Missing(ConfigurationPackagePayload manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Contents.Where(content => content is not null && !IsPresent(content, manifest.Configuration)).ToArray();
    }

    /// <summary>
    /// Imports the <see cref="Missing"/> content of <paramref name="manifest"/> from the publication folder. Every item ends in exactly
    /// one of <see cref="ContentSyncResult.Fetched"/>, <see cref="ContentSyncResult.Failed"/> or
    /// <see cref="ContentSyncResult.NotYetAvailable"/>; a failed item leaves nothing in its library.
    /// </summary>
    /// <param name="folder">Publication folder.</param>
    /// <param name="manifest">Verified publication payload; its master key decrypts the content.</param>
    /// <param name="progress">
    /// Receives the download (read and decrypt) of each missing item, then its install into each library it is listed for, in bytes
    /// against the sum of those weights (<see cref="InstallLength"/>), then a final report with an empty name.
    /// </param>
    /// <param name="ct">Stops between content items and decryption chunks; imported items stay imported.</param>
    /// <exception cref="ArgumentException">The manifest carries no 32-byte master key.</exception>
    /// <exception cref="OperationCanceledException">The fetch was cancelled; the next fetch resumes with the content still missing.</exception>
    public async Task<ContentSyncResult> FetchAsync(string folder, ConfigurationPackagePayload manifest, IProgress<ContentProgress>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.MasterKey is not { Length: MasterKeySize })
        {
            throw new ArgumentException("The publication master key must be 32 bytes.", nameof(manifest));
        }

        PackageContent[][] items = Missing(manifest).GroupBy(content => content.Hash, StringComparer.Ordinal).Select(group => group.ToArray()).ToArray();
        long total = items.Sum(Weight);
        Log.ForContext<ContentSync>().Information("Publication content fetch started. MissingCount={MissingCount}, TotalBytes={TotalBytes}",
            items.Sum(group => group.Length), items.Sum(group => Math.Max(group[0].Length, 0)));
        Tally tally = new();
        try
        {
            if (items.Length > 0)
            {
                await FetchItemsAsync(folder, manifest, items, total, progress, tally, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            Log.ForContext<ContentSync>().Information("Publication content fetch canceled. Fetched={Fetched}", tally.Fetched);
            throw;
        }

        ContentSyncResult result = new(tally.Fetched, tally.Failed, tally.NotYetAvailable);
        if (result.Failed.Count == 0 && result.NotYetAvailable.Count == 0)
        {
            Log.ForContext<ContentSync>().Information("Publication content fetch completed. Fetched={Fetched}", result.Fetched);
        }
        else
        {
            Log.ForContext<ContentSync>().Warning(
                "Publication content fetch incomplete. Fetched={Fetched}, FailedCount={FailedCount}, NotYetAvailableCount={NotYetAvailableCount}",
                result.Fetched, result.Failed.Count, result.NotYetAvailable.Count);
        }

        return result;
    }

    private async Task FetchItemsAsync(string folder, ConfigurationPackagePayload manifest, PackageContent[][] items, long total,
        IProgress<ContentProgress>? progress, Tally tally, CancellationToken cancellationToken)
    {
        PrivateDirectoryLease temporary;
        try
        {
            temporary = PrivateDirectory.CreateLeased(temporaryRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<ContentSync>().Warning(exception, "The publication content temporary directory is unavailable.");
            tally.NotYetAvailable.AddRange(items.SelectMany(group => group));
            return;
        }

        try
        {
            long done = 0;
            int loggedStep = 0;
            foreach (PackageContent[] group in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PackageContent first = group[0];
                progress?.Report(new(done, total, first.Name));
                await FetchItemAsync(folder, manifest, group, temporary.Path, progress, done, total, tally, cancellationToken).ConfigureAwait(false);
                // A failed or unavailable item still counts as handled, so progress never goes back.
                done = Math.Min(total, done + Weight(group));
                int step = total == 0 ? ProgressLogSteps : (int)(done * ProgressLogSteps / total);
                if (step > loggedStep)
                {
                    loggedStep = step;
                    Log.ForContext<ContentSync>().Debug(
                        "Publication content fetch progress. DoneWeight={DoneWeight}, ProgressWeight={ProgressWeight}", done, total);
                }
            }

            progress?.Report(new(total, total, string.Empty));
        }
        finally
        {
            try
            {
                temporary.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The temporary files were deleted on close; the empty directory is removed by the next abandoned cleanup.
                Log.ForContext<ContentSync>().Warning(exception, "The publication content temporary directory could not be removed.");
            }
        }
    }

    /// <summary>
    /// Decrypts one content file and imports it into the library of every kind it is listed under, reporting both steps from
    /// <paramref name="offset"/>.
    /// </summary>
    private async Task FetchItemAsync(string folder, ConfigurationPackagePayload manifest, PackageContent[] group, string temporaryDirectory,
        IProgress<ContentProgress>? progress, long offset, long total, Tally tally, CancellationToken cancellationToken)
    {
        string hash = group[0].Hash;
        if (!ContentTransfer.IsContentHash(hash))
        {
            tally.Failed.AddRange(group);
            return;
        }

        string source = PublicationFolder.ContentPath(folder, hash);
        if (RequiredTemporaryBytes(source, temporaryDirectory) is { } required)
        {
            Log.ForContext<ContentSync>().Warning("Not enough free space to decrypt publication content. Kind={Kind}, ContentHash={ContentHash}, RequiredBytes={RequiredBytes}",
                group[0].Kind, hash, required);
            tally.NotYetAvailable.AddRange(group);
            return;
        }

        FileStream plaintext;
        string plaintextHash;
        try
        {
            // Deleted when closed, on every path, including a crash of this process.
            plaintext = new FileStream(Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".tmp"), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<ContentSync>().Warning(exception, "A publication content temporary file could not be created.");
            tally.NotYetAvailable.AddRange(group);
            return;
        }

        await using (plaintext.ConfigureAwait(false))
        {
            byte[] key = ContentTransfer.DeriveContentKey(manifest.MasterKey!, hash);
            try
            {
                IProgress<long>? downloaded = progress is null
                    ? null
                    : new ByteProgress(progress, offset, Math.Max(group[0].Length, 0), total, group[0].Name, ContentProgressStep.Transferring);
                plaintextHash = await ContentTransfer.DecryptAsync(source, plaintext, key, downloaded, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Missing, still being delivered, or rewritten in place by "Reset publication": retried at the next check.
                Log.ForContext<ContentSync>().Debug("Publication content is not available yet. Kind={Kind}, ContentHash={ContentHash}", group[0].Kind, hash);
                tally.NotYetAvailable.AddRange(group);
                return;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            long installed = offset + Math.Max(group[0].Length, 0);
            foreach (PackageContent content in group)
            {
                plaintext.Position = 0;
                long length = InstallLength(content);
                IProgress<long>? installing = null;
                if (progress is not null && length > 0)
                {
                    progress.Report(new(installed, total, content.Name, ContentProgressStep.Installing));
                    installing = new ByteProgress(progress, installed, length, total, content.Name, ContentProgressStep.Installing);
                }

                await ImportItemAsync(content, manifest.Configuration, plaintext, plaintextHash, installing, tally, cancellationToken).ConfigureAwait(false);
                installed += length;
            }
        }
    }

    /// <summary>
    /// Imports one item and files it in exactly one bucket. An item the presence test of <see cref="Missing"/> still reports after
    /// its import (for example a file name its library stores under another name) is failed, so it is never counted as fetched and
    /// downloaded again at every check. Only cancellation escapes.
    /// </summary>
    private async Task ImportItemAsync(PackageContent content, FoundryConfigurationDocument configuration, FileStream plaintext, string plaintextHash,
        IProgress<long>? progress, Tally tally, CancellationToken cancellationToken)
    {
        try
        {
            await ImportAsync(content, configuration, plaintext, plaintextHash, progress, cancellationToken).ConfigureAwait(false);
            if (IsPresent(content, configuration))
            {
                tally.Fetched++;
                return;
            }

            Log.ForContext<ContentSync>().Warning("Publication content is still missing after its import. Kind={Kind}, ContentHash={ContentHash}",
                content.Kind, content.Hash);
            tally.Failed.Add(content);
        }
        catch (InvalidDataException exception)
        {
            Log.ForContext<ContentSync>().Warning(exception, "Publication content was rejected by its library. Kind={Kind}, ContentHash={ContentHash}",
                content.Kind, content.Hash);
            tally.Failed.Add(content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.ForContext<ContentSync>().Warning(exception, "Publication content could not be imported. Kind={Kind}, ContentHash={ContentHash}",
                content.Kind, content.Hash);
            tally.NotYetAvailable.Add(content);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Unexpected library or reader failures (for example from the image metadata reader): the item fails, the fetch goes on.
            Log.ForContext<ContentSync>().Warning("Publication content import failed unexpectedly. Kind={Kind}, ContentHash={ContentHash}, ExceptionType={ExceptionType}",
                content.Kind, content.Hash, exception.GetType().FullName);
            tally.Failed.Add(content);
        }
    }

    /// <summary>Imports one decrypted item; every check that can reject it runs before the library is changed.</summary>
    /// <exception cref="InvalidDataException">The plaintext does not match the listed hash or breaks the library rules.</exception>
    private async Task ImportAsync(PackageContent content, FoundryConfigurationDocument configuration, FileStream plaintext, string plaintextHash,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        switch (content.Kind)
        {
            case SharedContentKind.File:
                if (!plaintextHash.Equals(content.Hash, StringComparison.Ordinal) || plaintext.Length > AuthoringFileLibrary.MaximumFileBytes)
                {
                    throw new InvalidDataException("ContentSync.ContentMismatch");
                }

                byte[] bytes = new byte[plaintext.Length];
                await plaintext.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                foreach (AuthoringFileReference reference in FileReferences(configuration, content.Hash))
                {
                    files.Import(bytes, reference.FileName);
                }

                break;
            case SharedContentKind.Package:
                await ContentPackageArchive.ImportAsync(packages, plaintext, content.Hash, temporaryRoot, cancellationToken, progress).ConfigureAwait(false);
                break;
            case SharedContentKind.DriverSet:
                await ContentPackageArchive.ImportAsync(driverSets.Library, plaintext, content.Hash, temporaryRoot, cancellationToken, progress).ConfigureAwait(false);
                break;
            case SharedContentKind.Image:
                await ImportImageAsync(content, configuration, plaintext, progress, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException("ContentSync.UnsupportedContentKind");
        }
    }

    /// <summary>
    /// Imports an image plaintext: the metadata record must describe the listed hash and the WIM that follows it, and the configuration
    /// reference (whose id and display name become the library entry's) must have the same length; the library then checks the WIM.
    /// </summary>
    private async Task ImportImageAsync(PackageContent content, FoundryConfigurationDocument configuration, FileStream plaintext,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        CustomImageReference record = await LocalContentSource.ReadImageRecordAsync(plaintext, cancellationToken).ConfigureAwait(false);
        long wimLength = plaintext.Length - plaintext.Position;
        CustomImageReference reference = PortableConfiguration.UsedImages(configuration).FirstOrDefault(image => SameHash(image.ContentHash, content.Hash))
            ?? throw new InvalidDataException("ContentSync.ContentMismatch");
        if (!SameHash(record.ContentHash, content.Hash) || record.Length != wimLength || reference.Length != wimLength)
        {
            throw new InvalidDataException("ContentSync.ContentMismatch");
        }

        await images.ImportFromPublicationAsync(plaintext, reference, cancellationToken, progress).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the free space the decryption of <paramref name="source"/> needs on the temporary volume when that volume has less, or
    /// <see langword="null"/> when it has enough. The content file size bounds the plaintext from above (it adds a header and 28 bytes
    /// per chunk); <see cref="TemporarySpaceMargin"/> keeps room for the rest of the system. A content file or volume that cannot be
    /// measured is left to the decryption, which reports it.
    /// </summary>
    private static long? RequiredTemporaryBytes(string source, string temporaryDirectory)
    {
        try
        {
            long required = new FileInfo(source).Length + TemporarySpaceMargin;
            long available = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(temporaryDirectory))!).AvailableFreeSpace;
            return available < required ? required : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private bool IsPresent(PackageContent content, FoundryConfigurationDocument configuration) => content.Kind switch
    {
        SharedContentKind.File => FileReferences(configuration, content.Hash).All(files.IsAvailable),
        SharedContentKind.Package => PortableConfiguration.UsedPackages(configuration).Where(package => SameHash(package.ContentHash, content.Hash))
            .All(packages.IsAvailable),
        SharedContentKind.DriverSet => PortableConfiguration.UsedDriverSets(configuration).Where(package => SameHash(package.ContentHash, content.Hash))
            .All(driverSets.IsAvailable),
        SharedContentKind.Image => PortableConfiguration.UsedImages(configuration).Where(image => SameHash(image.ContentHash, content.Hash))
            .All(images.IsAvailable),
        _ => true
    };

    private static IEnumerable<AuthoringFileReference> FileReferences(FoundryConfigurationDocument configuration, string hash) =>
        ConfigurationContentReferences.AuthoringFiles(configuration).Where(reference => SameHash(reference.ContentHash, hash));

    private static bool SameHash(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    /// <summary>Outcome counters of one fetch.</summary>
    private sealed class Tally
    {
        public int Fetched { get; set; }

        public List<PackageContent> Failed { get; } = [];

        public List<PackageContent> NotYetAvailable { get; } = [];
    }

    /// <summary>
    /// Returns the bytes the install of <paramref name="content"/> handles: the checked extraction and the library import of a package
    /// or driver set (twice its length), the copy and hash of an image (its length), nothing for a file, which is small and read in memory.
    /// </summary>
    internal static long InstallLength(PackageContent content) => content.Kind switch
    {
        SharedContentKind.Package or SharedContentKind.DriverSet => 2 * Math.Max(content.Length, 0),
        SharedContentKind.Image => Math.Max(content.Length, 0),
        _ => 0
    };

    /// <summary>Returns the progress weight of one content file: its download plus its install into every library it is listed for.</summary>
    private static long Weight(PackageContent[] group) => Math.Max(group[0].Length, 0) + group.Sum(InstallLength);

    /// <summary>Maps the byte count of one step of one item, capped at the weight of that step, onto the progress of the whole fetch.</summary>
    private sealed class ByteProgress(IProgress<ContentProgress> target, long offset, long length, long total, string name, ContentProgressStep step)
        : IProgress<long>
    {
        public void Report(long value) => target.Report(new(Math.Min(total, offset + Math.Clamp(value, 0, length)), total, name, step));
    }
}
