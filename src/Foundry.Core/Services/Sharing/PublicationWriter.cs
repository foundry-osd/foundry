// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Sharing;

/// <summary>Opens the plaintext of the library content a publication lists.</summary>
public interface IContentSource
{
    /// <summary>
    /// Opens one content item for reading. The stream must be seekable (its length is fixed before encryption starts); for
    /// post-installation packages and driver sets it is the archive written by <see cref="ContentPackageArchive.WriteAsync"/>.
    /// The writer disposes it.
    /// </summary>
    Task<Stream> OpenAsync(PackageContent content, CancellationToken ct);
}

/// <summary>Everything one publish writes.</summary>
/// <param name="Folder">Publication folder.</param>
/// <param name="Configuration">Configuration already made portable by the caller.</param>
/// <param name="Contents">Library content the configuration references, already listed by the caller; hashes are 64 lowercase hex digits.</param>
/// <param name="Secrets">Vault entries to include, or <see langword="null"/> when passwords are not published.</param>
/// <param name="Signer">Reference signing key.</param>
/// <param name="MasterKey">32-byte key that content keys are derived from.</param>
/// <param name="Password">Publication password; must satisfy <see cref="PassphrasePolicy"/>.</param>
/// <param name="PublisherId">Stable publisher id.</param>
/// <param name="PublisherName">Display name of the reference PC.</param>
/// <param name="LastPublishedSequence">
/// Sequence of the last successful publish of this PC, persisted by the caller after each one (0 before the first). The folder
/// alone is not trusted for the next sequence: a folder writer can move <c>current.json</c> back or delete it.
/// </param>
public sealed record PublicationRequest(
    string Folder,
    FoundryConfigurationDocument Configuration,
    IReadOnlyList<PackageContent> Contents,
    IReadOnlyDictionary<string, string>? Secrets,
    PublicationSigningKey Signer,
    byte[] MasterKey,
    string Password,
    Guid PublisherId,
    string PublisherName,
    long LastPublishedSequence = 0);

/// <summary>Outcome of a publication write.</summary>
public enum PublishStatus
{
    /// <summary>The manifest was written and activated (or, for a rewrap, rewritten under the new password).</summary>
    Published,

    /// <summary>The current or the highest-numbered manifest of the folder is signed by another key; nothing was written.</summary>
    ForeignPublisher,

    /// <summary>
    /// The folder holds files that are not a publication this PC can continue: unrelated files, a pointer that is malformed, newer
    /// or leads to a missing or unsigned manifest, a sequence at the 18-digit name limit, or (for a rewrap) a current manifest that
    /// does not verify. Nothing was written.
    /// </summary>
    FolderNotEmpty,

    /// <summary>
    /// The folder is missing or could not be read or written, or (for a rewrap) holds no publication. A content source that fails
    /// with an <see cref="IOException"/> is reported the same way. The previous publication stays current.
    /// </summary>
    FolderUnavailable,

    /// <summary>The current password given to a rewrap does not open the current manifest.</summary>
    WrongPassword
}

/// <summary>Result of <see cref="PublicationWriter.PublishAsync"/>.</summary>
/// <param name="Status">Outcome.</param>
/// <param name="Sequence">The new sequence when published; the sequence of the foreign manifest for <see cref="PublishStatus.ForeignPublisher"/>; otherwise 0.</param>
/// <param name="ForeignKeyId">Key id of the foreign signer, set for <see cref="PublishStatus.ForeignPublisher"/>; read without the password, so not authenticated.</param>
public sealed record PublishResult(PublishStatus Status, long Sequence, string? ForeignKeyId);

/// <summary>Progress of the content copy of a publish.</summary>
/// <param name="Done">Plaintext bytes handled so far, including content already present in the folder.</param>
/// <param name="Total">Sum of the listed content lengths; <paramref name="Done"/> never exceeds it.</param>
/// <param name="CurrentName">Display name of the content being written, empty in the final report.</param>
public sealed record ContentProgress(long Done, long Total, string CurrentName);

/// <summary>
/// Writes publications to a folder (spec 5.5 and 6.1): encrypted content, then the signed manifest, then <c>current.json</c>, so a
/// manifest is activated only when everything it lists is in place. Keeps the current and previous manifests and deletes content
/// files that neither references.
/// </summary>
public static class PublicationWriter
{
    private const int MasterKeySize = 32;

    /// <summary>
    /// Publishes a new version. The folder must be empty, or its current manifest (named by <c>current.json</c>) and its
    /// highest-numbered signed manifest must be signed by <see cref="PublicationRequest.Signer"/>; the signer is read from the
    /// envelope, no password needed. A folder without <c>current.json</c> that still holds manifests is an existing publication, not
    /// an empty folder. The new sequence is one above the highest of <see cref="PublicationRequest.LastPublishedSequence"/>, the
    /// pointer sequence and every valid manifest name in <c>manifests/</c>, so a moved or deleted pointer can never make followers
    /// skip or reject a new version. Content is keyed by hash only: a hash listed several times (for example as a package and a
    /// driver set) is written once, and a content file already complete under the current master key is not rewritten.
    /// </summary>
    /// <param name="request">What to publish.</param>
    /// <param name="contents">Opens each content item that is not in the folder yet.</param>
    /// <param name="progress">Receives content copy progress.</param>
    /// <param name="cancellationToken">Stops before activation; the previous pointer stays current.</param>
    /// <exception cref="ArgumentException">The password breaks the policy, the master key is not 32 bytes, or a content hash is not lowercase hex.</exception>
    /// <exception cref="InvalidDataException">A content stream changed length while it was encrypted.</exception>
    public static async Task<PublishResult> PublishAsync(PublicationRequest request, IContentSource contents, IProgress<ContentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(contents);
        ValidateRequest(request);
        if (!Directory.Exists(request.Folder))
        {
            return new(PublishStatus.FolderUnavailable, 0, null);
        }

        try
        {
            FolderState state = await InspectAsync(request.Folder, request.Signer.KeyId, cancellationToken).ConfigureAwait(false);
            if (state.Status is { } status)
            {
                return new(status, status == PublishStatus.ForeignPublisher ? state.Sequence : 0, state.ForeignKeyId);
            }

            long highest = Math.Max(request.LastPublishedSequence, state.Sequence);
            if (highest >= PublicationFolder.MaximumSequence)
            {
                return new(PublishStatus.FolderNotEmpty, 0, null);
            }

            long sequence = highest + 1;
            await WriteContentsAsync(request, contents, progress, cancellationToken).ConfigureAwait(false);
            string manifest = PublicationFolder.NewManifest(sequence);
            byte[] manifestBytes = ConfigurationPackageCodec.Write(CreatePayload(request, sequence), request.Password, request.Signer);
            cancellationToken.ThrowIfCancellationRequested();
            await PublicationFolder.WriteAtomicAsync(PublicationFolder.ManifestPath(request.Folder, manifest), manifestBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await PublicationFolder.WritePointerAsync(request.Folder, manifest, CancellationToken.None).ConfigureAwait(false);
            try
            {
                RemoveUnreferenced(request, manifest, sequence, state.Previous);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The new version is active; the next publish retries the cleanup.
            }

            return new(PublishStatus.Published, sequence, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(PublishStatus.FolderUnavailable, 0, null);
        }
    }

    /// <summary>
    /// "Change password": rewrites the current manifest, unchanged, under <paramref name="newPassword"/> in place (same name and
    /// sequence, so followers that applied it do not reload it). The pointer and the content files are untouched; the previous
    /// manifest keeps the old password.
    /// </summary>
    /// <param name="folder">Publication folder.</param>
    /// <param name="oldPassword">Password of the current manifest.</param>
    /// <param name="newPassword">New password; must satisfy <see cref="PassphrasePolicy"/>.</param>
    /// <param name="signer">Reference signing key; the current manifest must be signed with it.</param>
    /// <param name="cancellationToken">Stops before the manifest is replaced.</param>
    /// <returns>
    /// <see cref="PublishStatus.Published"/>, <see cref="PublishStatus.WrongPassword"/>, <see cref="PublishStatus.ForeignPublisher"/>,
    /// <see cref="PublishStatus.FolderNotEmpty"/> or <see cref="PublishStatus.FolderUnavailable"/> (also when <c>current.json</c> is missing).
    /// </returns>
    /// <exception cref="ArgumentException">The new password breaks the policy.</exception>
    public static async Task<PublishStatus> RewrapAsync(string folder, string oldPassword, string newPassword, PublicationSigningKey signer,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(oldPassword);
        ArgumentNullException.ThrowIfNull(signer);
        ValidatePassword(newPassword);
        if (!Directory.Exists(folder))
        {
            return PublishStatus.FolderUnavailable;
        }

        try
        {
            FolderState state = await InspectAsync(folder, signer.KeyId, cancellationToken).ConfigureAwait(false);
            if (state.Status is { } status)
            {
                return status;
            }

            if (state.Current is null)
            {
                return PublishStatus.FolderUnavailable;
            }

            PackageReadResult read = ConfigurationPackageCodec.Read(state.Current.Bytes, oldPassword, signer.PublicKey);
            if (read.Status == PackageReadStatus.WrongPassword)
            {
                return PublishStatus.WrongPassword;
            }

            if (read.Status != PackageReadStatus.Ok || read.Payload!.Kind != PackageKind.Publication)
            {
                return PublishStatus.FolderNotEmpty;
            }

            byte[] rewrapped = ConfigurationPackageCodec.Write(read.Payload, newPassword, signer);
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(PublicationFolder.ManifestsDirectory(folder), state.Current.FileName);
            await PublicationFolder.WriteAtomicAsync(path, rewrapped, cancellationToken).ConfigureAwait(false);
            return PublishStatus.Published;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PublishStatus.FolderUnavailable;
        }
    }

    private static void ValidateRequest(PublicationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Folder);
        ArgumentNullException.ThrowIfNull(request.Configuration);
        ArgumentNullException.ThrowIfNull(request.Contents);
        ArgumentNullException.ThrowIfNull(request.Signer);
        ValidatePassword(request.Password);
        if (request.MasterKey is not { Length: MasterKeySize })
        {
            throw new ArgumentException("The publication master key must be 32 bytes.", nameof(request));
        }

        if (request.Contents.Any(content => content is null || !ContentTransfer.IsContentHash(content.Hash)))
        {
            throw new ArgumentException("Every publication content hash must be 64 lowercase hexadecimal digits.", nameof(request));
        }

        if (request.LastPublishedSequence is < 0 or >= PublicationFolder.MaximumSequence)
        {
            throw new ArgumentException("The last published sequence is out of range.", nameof(request));
        }
    }

    private static void ValidatePassword(string password)
    {
        if (PassphrasePolicy.Validate(password) != PassphraseValidation.Valid)
        {
            throw new ArgumentException("The publication password does not satisfy the passphrase policy.", nameof(password));
        }
    }

    /// <summary>
    /// Decides whether this key may continue the folder, from <c>current.json</c> and the envelope signer (no password) of every
    /// validly named manifest. The pointer can be moved back or deleted by a folder writer, so the highest-numbered signed manifest
    /// decides ownership too, and every manifest name counts toward the next sequence.
    /// </summary>
    private static async Task<FolderState> InspectAsync(string folder, string keyId, CancellationToken cancellationToken)
    {
        PublicationPointerRead pointer = await PublicationFolder.ReadPointerAsync(folder, cancellationToken).ConfigureAwait(false);
        if (pointer.State is PublicationPointerState.Invalid or PublicationPointerState.Unsupported
            || (pointer.State == PublicationPointerState.Missing && !PublicationFolder.HoldsOnlyPublicationFiles(folder)))
        {
            return FolderState.NotEmpty;
        }

        IReadOnlyList<ManifestFile> manifests = await ReadManifestsAsync(folder, cancellationToken).ConfigureAwait(false);
        ManifestFile? current = null;
        if (pointer.State == PublicationPointerState.Ok)
        {
            string fileName = PublicationFolder.ManifestFileName(pointer.Manifest!);
            current = manifests.FirstOrDefault(manifest => manifest.FileName == fileName);
            if (current?.KeyId is null)
            {
                return FolderState.NotEmpty;
            }
        }

        ManifestFile? latest = manifests.Where(manifest => manifest.KeyId is not null).MaxBy(manifest => manifest.Sequence);
        ManifestFile? foreign = new[] { current, latest }.FirstOrDefault(manifest => manifest is not null && manifest.KeyId != keyId);
        if (foreign is not null)
        {
            return new(PublishStatus.ForeignPublisher, foreign.Sequence, foreign.KeyId, null, null);
        }

        long highest = manifests.Select(manifest => manifest.Sequence).Append(pointer.Sequence).Max();
        ManifestFile? previous = manifests.Where(manifest => manifest.KeyId == keyId).MaxBy(manifest => manifest.Sequence);
        return new(null, highest, null, current, previous);
    }

    /// <summary>Reads every validly named file of <c>manifests/</c>; a file over the package size limit gets no bytes and no signer.</summary>
    private static async Task<IReadOnlyList<ManifestFile>> ReadManifestsAsync(string folder, CancellationToken cancellationToken)
    {
        string directory = PublicationFolder.ManifestsDirectory(folder);
        List<ManifestFile> manifests = [];
        if (!Directory.Exists(directory))
        {
            return manifests;
        }

        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string fileName = Path.GetFileName(path);
            if (!PublicationFolder.TryParseManifestFileName(fileName, out long sequence))
            {
                continue;
            }

            byte[] bytes = new FileInfo(path).Length <= ConfigurationPackageCodec.MaximumPackageBytes
                ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)
                : [];
            manifests.Add(new(fileName, sequence, ConfigurationPackageCodec.ReadSignerKeyId(bytes), bytes));
        }

        return manifests;
    }

    private static async Task WriteContentsAsync(PublicationRequest request, IContentSource source, IProgress<ContentProgress>? progress,
        CancellationToken cancellationToken)
    {
        PackageContent[] distinct = request.Contents.DistinctBy(content => content.Hash, StringComparer.Ordinal).ToArray();
        long total = distinct.Sum(content => Math.Max(content.Length, 0));
        long done = 0;
        Directory.CreateDirectory(PublicationFolder.ContentDirectory(request.Folder));
        Directory.CreateDirectory(PublicationFolder.ManifestsDirectory(request.Folder));
        foreach (PackageContent content in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(done, total, content.Name));
            string path = PublicationFolder.ContentPath(request.Folder, content.Hash);
            byte[] key = ContentTransfer.DeriveContentKey(request.MasterKey, content.Hash);
            try
            {
                if (!ContentTransfer.IsComplete(path, key))
                {
                    await using Stream stream = await source.OpenAsync(content, cancellationToken).ConfigureAwait(false);
                    IProgress<long>? bytes = progress is null ? null : new ByteProgress(progress, done, total, content.Name);
                    await ContentTransfer.EncryptAsync(stream, path, key, bytes, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            done = Math.Min(total, done + Math.Max(content.Length, 0));
        }

        progress?.Report(new(total, total, string.Empty));
    }

    private static ConfigurationPackagePayload CreatePayload(PublicationRequest request, long sequence) => new()
    {
        Kind = PackageKind.Publication,
        CreatedUtc = DateTimeOffset.UtcNow,
        Sequence = sequence,
        Publisher = new PackagePublisher(request.PublisherId, request.PublisherName, request.Signer.KeyId, request.Signer.PublicKey),
        Configuration = request.Configuration,
        Secrets = request.Secrets,
        Contents = request.Contents,
        MasterKey = request.MasterKey
    };

    /// <summary>
    /// After activation, keeps the new manifest, the previous one (the highest-numbered manifest of this key before the publish) and
    /// any manifest numbered above the new sequence, and deletes the other manifests; then deletes content files neither kept manifest
    /// lists, and the <c>.tmp</c> leftovers of interrupted writes. The previous manifest's list is read with the request password and
    /// the signer key; when that fails no content file is deleted. A file that cannot be deleted (for example while a follower reads
    /// it) is left for the next publish.
    /// </summary>
    private static void RemoveUnreferenced(PublicationRequest request, string manifest, long sequence, ManifestFile? previous)
    {
        HashSet<string> keptManifests = new(StringComparer.Ordinal) { PublicationFolder.ManifestFileName(manifest) };
        HashSet<string> referenced = new(request.Contents.Select(content => content.Hash), StringComparer.Ordinal);
        bool referencesKnown = true;
        if (previous is not null)
        {
            keptManifests.Add(previous.FileName);
            PackageReadResult read = ConfigurationPackageCodec.Read(previous.Bytes, request.Password, request.Signer.PublicKey);
            referencesKnown = read.Status == PackageReadStatus.Ok;
            if (referencesKnown)
            {
                referenced.UnionWith(read.Payload!.Contents.Select(content => content.Hash));
            }
        }

        DeleteFiles(PublicationFolder.ManifestsDirectory(request.Folder), name => PublicationFolder.WithoutTemporaryExtension(name) is { } target
            ? PublicationFolder.TryParseManifestFileName(target, out _)
            : PublicationFolder.TryParseManifestFileName(name, out long found) && found <= sequence && !keptManifests.Contains(name));
        DeleteFiles(PublicationFolder.ContentDirectory(request.Folder), name => PublicationFolder.WithoutTemporaryExtension(name) is { } target
            ? PublicationFolder.ContentHashOf(target) is not null
            : referencesKnown && PublicationFolder.ContentHashOf(name) is { } hash && !referenced.Contains(hash));
        PublicationFolder.DeleteBestEffort(PublicationFolder.PointerTemporaryPath(request.Folder));
    }

    private static void DeleteFiles(string directory, Func<string, bool> shouldDelete)
    {
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            if (shouldDelete(Path.GetFileName(path)))
            {
                PublicationFolder.DeleteBestEffort(path);
            }
        }
    }

    /// <summary>A validly named file of <c>manifests/</c>.</summary>
    /// <param name="FileName">File name, <c>&lt;sequence&gt;-&lt;id&gt;.fcfg</c>.</param>
    /// <param name="Sequence">Sequence parsed from the name (not authenticated).</param>
    /// <param name="KeyId">Envelope signature key id (not authenticated), or null when the file is not a signed envelope.</param>
    /// <param name="Bytes">File content, empty when over the package size limit.</param>
    private sealed record ManifestFile(string FileName, long Sequence, string? KeyId, byte[] Bytes);

    /// <summary>What <see cref="InspectAsync"/> found.</summary>
    /// <param name="Status">Set when the folder cannot be continued by this key.</param>
    /// <param name="Sequence">Highest sequence in the folder (pointer and manifest names), or the foreign manifest's sequence.</param>
    /// <param name="ForeignKeyId">Signer of the deciding manifest when it is another key.</param>
    /// <param name="Current">Manifest named by <c>current.json</c>, when present.</param>
    /// <param name="Previous">Highest-numbered manifest signed (by envelope key id) with this key, kept by the next cleanup.</param>
    private sealed record FolderState(PublishStatus? Status, long Sequence, string? ForeignKeyId, ManifestFile? Current, ManifestFile? Previous)
    {
        public static FolderState NotEmpty { get; } = new(PublishStatus.FolderNotEmpty, 0, null, null, null);
    }

    /// <summary>Maps the encrypted byte count of one content item onto the progress of the whole publish.</summary>
    private sealed class ByteProgress(IProgress<ContentProgress> target, long offset, long total, string name) : IProgress<long>
    {
        public void Report(long value) => target.Report(new(Math.Min(total, offset + value), total, name));
    }
}
