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
    /// post-installation packages and driver sets it is the archive written by <see cref="ContentPackageArchive.WriteAsync"/>, and for
    /// images the header, metadata record and WIM described by <see cref="LocalContentSource"/>, so their bytes do not hash to
    /// <see cref="PackageContent.Hash"/> (the hash of the package manifest or of the WIM); for files they do. The writer disposes it.
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
/// <param name="TakeOver">
/// "Take over" (spec 6.1): when the folder belongs to another key, continue it with <see cref="Signer"/> instead of returning
/// <see cref="PublishStatus.ForeignPublisher"/>. The foreign manifest that decides must open with <see cref="Password"/> and verify
/// with its own publisher key; followers then have to accept the new reference once. Ignored when the folder is not foreign.
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
    long LastPublishedSequence = 0,
    bool TakeOver = false);

/// <summary>Outcome of a publication write.</summary>
public enum PublishStatus
{
    /// <summary>The manifest was written and activated (or, for a rewrap, rewritten under the new password).</summary>
    Published,

    /// <summary>
    /// The folder was taken over by another key: the manifest <c>current.json</c> names is signed by another key, a validly named
    /// manifest of another key sits above the highest manifest of this PC, or no manifest of this PC authenticates and one of another
    /// key is present. Nothing was written.
    /// </summary>
    ForeignPublisher,

    /// <summary>
    /// The folder holds files that are not a publication this PC can continue: unrelated files and no manifest of this PC, or a
    /// pointer of a newer format. Nothing was written.
    /// </summary>
    FolderNotEmpty,

    /// <summary>
    /// The folder is missing or could not be read or written, or (for a rewrap) holds no publication at all. A content source that
    /// fails with an <see cref="IOException"/> is reported the same way. The previous publication stays current.
    /// </summary>
    FolderUnavailable,

    /// <summary>
    /// The highest-numbered manifest whose signature verifies with this PC's key does not decrypt with the password (for a rewrap:
    /// the current password given is wrong; for a publish: the caller holds an outdated password). Older manifests are never used
    /// instead, which would roll the publication back. Nothing was written.
    /// </summary>
    WrongPassword,

    /// <summary>
    /// The next sequence would not fit the 18-digit manifest name; the publication cannot continue and needs a new folder. Only
    /// authenticated sequences count, so files planted in the folder cannot cause it.
    /// </summary>
    SequenceExhausted
}

/// <summary>Result of <see cref="PublicationWriter.PublishAsync"/>.</summary>
/// <param name="Status">Outcome.</param>
/// <param name="Sequence">The new sequence when published; the sequence in the name of the foreign manifest for <see cref="PublishStatus.ForeignPublisher"/>; otherwise 0.</param>
/// <param name="ForeignKeyId">Key id of the foreign signer, set for <see cref="PublishStatus.ForeignPublisher"/>; read without the password, so not authenticated.</param>
public sealed record PublishResult(PublishStatus Status, long Sequence, string? ForeignKeyId);

/// <summary>Progress of the content copy of a publish (<see cref="PublicationWriter"/>) or a fetch (<see cref="ContentSync"/>).</summary>
/// <param name="Done">Plaintext bytes handled so far, including content already present in the folder.</param>
/// <param name="Total">Sum of the listed lengths over distinct content hashes; <paramref name="Done"/> never exceeds it.</param>
/// <param name="CurrentName">Display name of the content being written, empty in the final report.</param>
public sealed record ContentProgress(long Done, long Total, string CurrentName);

/// <summary>
/// Writes publications to a folder (spec 5.5 and 6.1): encrypted content, then the signed manifest, then <c>current.json</c>, so a
/// manifest is activated only when everything it lists is in place. Keeps the current and previous manifests and deletes content
/// files that neither references.
/// </summary>
/// <remarks>
/// Folder state comes from authenticated manifests only. Manifest names, <c>current.json</c> and envelope key ids are writable by
/// anyone with write access to the folder, so a manifest counts as this PC's only when its signature verifies with this PC's key,
/// its payload decrypts with the password, it is a publication, and its payload sequence equals the sequence in its name. Other
/// files never decide the next sequence or the previous manifest; envelopes signed by another key are read only to detect a take
/// over. A folder writer can still block publishing with a forged envelope carrying another key id (named by the pointer, or above
/// the highest manifest of this PC) or with a copy of an old-password manifest above the real ones. That is denial of service
/// only: it can never roll the publication back, make it use an outdated password, or let it overwrite a real take over.
/// </remarks>
public static class PublicationWriter
{
    private const int MasterKeySize = 32;

    /// <summary>
    /// Publishes a new version. The folder is foreign when the manifest <c>current.json</c> names is signed by another key, when a
    /// manifest of another key sits above the highest manifest of this PC, or when no manifest of this PC authenticates and one of
    /// another key is present. A foreign folder returns <see cref="PublishStatus.ForeignPublisher"/>, unless
    /// <see cref="PublicationRequest.TakeOver"/> is set: the deciding foreign manifest must then open with the request password
    /// (<see cref="PublishStatus.WrongPassword"/> otherwise) and be an authenticated publication under its own publisher key
    /// (<see cref="PublishStatus.ForeignPublisher"/> otherwise); the publish continues above it and keeps it as the previous manifest,
    /// so content it does not share with this publish survives one more version. Shared content is re-encrypted in place under the new
    /// master key, so the foreign manifest can no longer decrypt it: a take over cancelled after the content step leaves the foreign
    /// publication's followers failing on those hashes until a retry or the foreign PC's next publish.
    /// Otherwise <see cref="PublishStatus.WrongPassword"/> is returned when the highest manifest signed with this PC's key does not
    /// decrypt with the request password, and the publish continues from the highest
    /// authenticated manifest of this PC (see the class remarks), whatever the pointer says, or starts a new publication when the
    /// folder holds only publication files. The new sequence is one above the highest of
    /// <see cref="PublicationRequest.LastPublishedSequence"/>, this PC's manifest and, on a take over, the foreign manifest. Content is
    /// keyed by hash only: a hash listed several times (for example as a package and a driver set) is written once, and a content file
    /// already complete under the current master key is not rewritten (after a take over, content under the old master key is).
    /// </summary>
    /// <param name="request">What to publish.</param>
    /// <param name="contents">Opens each content item that is not in the folder yet.</param>
    /// <param name="progress">Receives content copy progress.</param>
    /// <param name="cancellationToken">Stops before activation; the previous pointer stays current.</param>
    /// <exception cref="ArgumentException">
    /// The password breaks the policy, the master key is not 32 bytes, a content hash is not lowercase hex, the last published
    /// sequence is negative, or a content stream returned by <paramref name="contents"/> is not seekable.
    /// </exception>
    /// <exception cref="InvalidDataException">A content stream changed length while it was encrypted.</exception>
    /// <exception cref="OperationCanceledException">Cancelled before activation; the previous pointer stays current.</exception>
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
            FolderState state = await InspectAsync(request.Folder, request.Signer, request.Password, cancellationToken).ConfigureAwait(false);
            if (state.Status == PublishStatus.ForeignPublisher && request.TakeOver)
            {
                state = await TakeOverAsync(state, request.Password, cancellationToken).ConfigureAwait(false);
            }

            if (state.Status is { } status)
            {
                return new(status, status == PublishStatus.ForeignPublisher ? state.Sequence : 0, state.ForeignKeyId);
            }

            long highest = Math.Max(request.LastPublishedSequence, state.Sequence);
            if (highest >= PublicationFolder.MaximumSequence)
            {
                return new(PublishStatus.SequenceExhausted, 0, null);
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
    /// "Change password": rewrites the highest authenticated manifest of this PC (see the class remarks), unchanged, under
    /// <paramref name="newPassword"/> in place (same name and sequence, so followers that applied it do not reload it), then points
    /// <c>current.json</c> at it again, which undoes a pointer moved back by a folder writer. Content files are untouched; older
    /// manifests keep the old password.
    /// </summary>
    /// <param name="folder">Publication folder.</param>
    /// <param name="oldPassword">Password of the current manifest.</param>
    /// <param name="newPassword">New password; must satisfy <see cref="PassphrasePolicy"/>.</param>
    /// <param name="signer">Reference signing key.</param>
    /// <param name="cancellationToken">Stops before the manifest is replaced.</param>
    /// <returns>
    /// <see cref="PublishStatus.Published"/>, <see cref="PublishStatus.WrongPassword"/>, <see cref="PublishStatus.ForeignPublisher"/>,
    /// <see cref="PublishStatus.FolderNotEmpty"/> or <see cref="PublishStatus.FolderUnavailable"/>.
    /// <see cref="PublishStatus.ForeignPublisher"/> follows the same rules as for a publish (a rewrap never takes over);
    /// <see cref="PublishStatus.FolderUnavailable"/> means an I/O failure or a folder holding no publication.
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
            FolderState state = await InspectAsync(folder, signer, oldPassword, cancellationToken).ConfigureAwait(false);
            if (state.Status is { } status)
            {
                return status;
            }

            if (state.Previous is null)
            {
                return PublishStatus.FolderUnavailable;
            }

            byte[] rewrapped = ConfigurationPackageCodec.Write(state.Previous.Payload, newPassword, signer);
            cancellationToken.ThrowIfCancellationRequested();
            string manifest = PublicationFolder.ManifestPointerPath(state.Previous.FileName);
            await PublicationFolder.WriteAtomicAsync(PublicationFolder.ManifestPath(folder, manifest), rewrapped, cancellationToken).ConfigureAwait(false);
            await PublicationFolder.WritePointerAsync(folder, manifest, CancellationToken.None).ConfigureAwait(false);
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

        if (request.LastPublishedSequence < 0)
        {
            throw new ArgumentException("The last published sequence cannot be negative.", nameof(request));
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
    /// Decides whether this PC may continue the folder and from which manifest (see the class remarks). Manifests are walked from
    /// the highest name down (on equal sequences the file <c>current.json</c> names first); the first one whose signature verifies
    /// with this PC's key decides:
    /// <list type="bullet">
    /// <item>it decrypts and its payload sequence equals its name: it is the previous manifest;</item>
    /// <item>it decrypts but its payload sequence differs (a renamed copy): it is skipped and the walk goes on;</item>
    /// <item>it does not decrypt with the password: <see cref="PublishStatus.WrongPassword"/>, never a fall back to an older manifest,
    /// which would roll the publication back under an outdated password.</item>
    /// </list>
    /// A foreign key is reported when <c>current.json</c> names a manifest signed by another key, when a manifest of another key sits
    /// above the manifest of this PC that decided the walk, or, when no manifest of this PC authenticates, when any validly named
    /// manifest is signed by another key: neither PC may overwrite a take over, even after <c>current.json</c> was deleted.
    /// Byte-identical candidates are decrypted once, so planted copies cost one key derivation in total.
    /// </summary>
    private static async Task<FolderState> InspectAsync(string folder, PublicationSigningKey signer, string password, CancellationToken cancellationToken)
    {
        PublicationPointerRead pointer = await PublicationFolder.ReadPointerAsync(folder, cancellationToken).ConfigureAwait(false);
        if (pointer.State == PublicationPointerState.Unsupported)
        {
            return FolderState.NotEmpty;
        }

        string? pointedFileName = pointer.State == PublicationPointerState.Ok ? PublicationFolder.ManifestFileName(pointer.Manifest!) : null;
        IReadOnlyList<ManifestName> manifests = ListManifests(folder, pointedFileName);
        ManifestName? pointed = manifests.FirstOrDefault(manifest => manifest.FileName == pointedFileName);
        string? pointedKeyId = pointed is null
            ? null
            : ConfigurationPackageCodec.ReadSignerKeyId(await ReadManifestAsync(pointed, cancellationToken).ConfigureAwait(false));

        Dictionary<string, PackageReadResult> reads = new(StringComparer.Ordinal);
        ManifestName? foreign = null;
        string? foreignKeyId = null;
        ManifestName? deciding = null;
        FolderState? decision = null;
        foreach (ManifestName manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = await ReadManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
            string digest = Convert.ToHexString(SHA256.HashData(bytes));
            if (!reads.TryGetValue(digest, out PackageReadResult? read))
            {
                read = ConfigurationPackageCodec.Read(bytes, password, signer.PublicKey);
                reads.Add(digest, read);
            }

            if (read is { Status: PackageReadStatus.Ok, Payload: { Kind: PackageKind.Publication } payload } && payload.Sequence == manifest.Sequence)
            {
                (deciding, decision) = (manifest, new(null, manifest.Sequence, null, new AuthenticatedManifest(manifest.FileName, payload)));
                break;
            }

            if (read.Status == PackageReadStatus.WrongPassword)
            {
                (deciding, decision) = (manifest, new(PublishStatus.WrongPassword, 0, null, null));
                break;
            }

            if (foreign is null && read.Status == PackageReadStatus.InvalidSignature && read.SignerKeyId is { } keyId && keyId != signer.KeyId)
            {
                (foreign, foreignKeyId) = (manifest, keyId);
            }
        }

        if (pointedKeyId is not null && pointedKeyId != signer.KeyId)
        {
            (foreign, foreignKeyId) = (pointed, pointedKeyId);
        }
        else if (deciding is not null && foreign is not null && foreign.Sequence <= deciding.Sequence)
        {
            foreign = null;
        }

        if (foreign is not null)
        {
            return new(PublishStatus.ForeignPublisher, foreign.Sequence, foreignKeyId, decision?.Previous, foreign);
        }

        return decision ?? (PublicationFolder.HoldsOnlyPublicationFiles(folder) ? FolderState.Empty : FolderState.NotEmpty);
    }

    /// <summary>
    /// "Take over" (spec 6.1): accepts the foreign manifest that made the folder foreign when it opens with the request password and
    /// verifies with the publisher key it embeds, and continues above it and above this PC's own manifest. The foreign manifest
    /// becomes the previous manifest, so content it does not share with this publish survives one more version; shared content is
    /// re-encrypted in place under the new master key, and a take over cancelled after the content step leaves the foreign
    /// publication's followers failing on those hashes until a retry or the foreign PC's next publish.
    /// </summary>
    private static async Task<FolderState> TakeOverAsync(FolderState foreign, string password, CancellationToken cancellationToken)
    {
        ManifestName manifest = foreign.Foreign!;
        PackageReadResult read = ConfigurationPackageCodec.Read(await ReadManifestAsync(manifest, cancellationToken).ConfigureAwait(false), password);
        if (read.Status == PackageReadStatus.WrongPassword)
        {
            return new(PublishStatus.WrongPassword, 0, null, null);
        }

        if (read is not { Status: PackageReadStatus.Ok, Payload: { Kind: PackageKind.Publication } payload } || payload.Sequence != manifest.Sequence)
        {
            return foreign;
        }

        long sequence = Math.Max(manifest.Sequence, foreign.Previous?.Payload.Sequence ?? 0);
        return new(null, sequence, null, new AuthenticatedManifest(manifest.FileName, payload));
    }

    /// <summary>
    /// Lists the validly named files of <c>manifests/</c>, highest sequence first; on equal sequences <paramref name="pointedFileName"/>
    /// comes first, so cleanup never deletes the pointed file in favour of a same-sequence copy.
    /// </summary>
    private static IReadOnlyList<ManifestName> ListManifests(string folder, string? pointedFileName)
    {
        string directory = PublicationFolder.ManifestsDirectory(folder);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<ManifestName> manifests = [];
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            string fileName = Path.GetFileName(path);
            if (PublicationFolder.TryParseManifestFileName(fileName, out long sequence))
            {
                manifests.Add(new(fileName, sequence, path));
            }
        }

        return manifests.OrderByDescending(manifest => manifest.Sequence)
            .ThenByDescending(manifest => manifest.FileName == pointedFileName)
            .ThenBy(manifest => manifest.FileName, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Reads a manifest file; one over the package size limit reads as empty, which the codec rejects.</summary>
    private static async Task<byte[]> ReadManifestAsync(ManifestName manifest, CancellationToken cancellationToken)
    {
        return new FileInfo(manifest.Path).Length <= ConfigurationPackageCodec.MaximumPackageBytes
            ? await File.ReadAllBytesAsync(manifest.Path, cancellationToken).ConfigureAwait(false)
            : [];
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
                    IProgress<long>? bytes = progress is null ? null : new ByteProgress(progress, done, Math.Max(content.Length, 0), total, content.Name);
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
    /// After activation, keeps the new manifest, the previous authenticated one and any manifest numbered above the new sequence,
    /// and deletes the other manifests (the pointer now names the new one, so none of them is referenced); then deletes content files
    /// neither kept manifest lists, and the <c>.tmp</c> leftovers of interrupted writes. A planted copy is never the previous
    /// manifest, so it cannot cause the real previous manifest or its content to be deleted. A file that cannot be deleted (for
    /// example while a follower reads it) is left for the next publish.
    /// </summary>
    private static void RemoveUnreferenced(PublicationRequest request, string manifest, long sequence, AuthenticatedManifest? previous)
    {
        HashSet<string> keptManifests = new(StringComparer.Ordinal) { PublicationFolder.ManifestFileName(manifest) };
        HashSet<string> referenced = new(request.Contents.Select(content => content.Hash), StringComparer.Ordinal);
        if (previous is not null)
        {
            keptManifests.Add(previous.FileName);
            referenced.UnionWith(previous.Payload.Contents.Select(content => content.Hash));
        }

        DeleteFiles(PublicationFolder.ManifestsDirectory(request.Folder), name => PublicationFolder.WithoutTemporaryExtension(name) is { } target
            ? PublicationFolder.TryParseManifestFileName(target, out _)
            : PublicationFolder.TryParseManifestFileName(name, out long found) && found <= sequence && !keptManifests.Contains(name));
        DeleteFiles(PublicationFolder.ContentDirectory(request.Folder), name => PublicationFolder.WithoutTemporaryExtension(name) is { } target
            ? PublicationFolder.ContentHashOf(target) is not null
            : PublicationFolder.ContentHashOf(name) is { } hash && !referenced.Contains(hash));
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

    /// <summary>A validly named file of <c>manifests/</c>; nothing about it is authenticated.</summary>
    /// <param name="FileName">File name, <c>&lt;sequence&gt;-&lt;id&gt;.fcfg</c>.</param>
    /// <param name="Sequence">Sequence parsed from the name.</param>
    /// <param name="Path">Local path.</param>
    private sealed record ManifestName(string FileName, long Sequence, string Path);

    /// <summary>A manifest of this PC: signature verified with its key, decrypted with the password, payload sequence equal to the name.</summary>
    /// <param name="FileName">File name in <c>manifests/</c>.</param>
    /// <param name="Payload">Decrypted payload.</param>
    private sealed record AuthenticatedManifest(string FileName, ConfigurationPackagePayload Payload);

    /// <summary>What <see cref="InspectAsync"/> (or <see cref="TakeOverAsync"/>) found.</summary>
    /// <param name="Status">Set when the publish or rewrap cannot go on.</param>
    /// <param name="Sequence">
    /// Sequence to continue above: that of <paramref name="Previous"/> (0 without one), on a take over the highest of the foreign and
    /// this PC's manifest; for <see cref="PublishStatus.ForeignPublisher"/>, the sequence in the name of <paramref name="Foreign"/>.
    /// </param>
    /// <param name="ForeignKeyId">Envelope key id of <paramref name="Foreign"/> (not authenticated).</param>
    /// <param name="Previous">
    /// Manifest kept by the next cleanup: the highest authenticated manifest of this PC, or after a take over the foreign manifest.
    /// </param>
    /// <param name="Foreign">The manifest of another key that made the folder foreign, the one a take over must open.</param>
    private sealed record FolderState(PublishStatus? Status, long Sequence, string? ForeignKeyId, AuthenticatedManifest? Previous, ManifestName? Foreign = null)
    {
        public static FolderState Empty { get; } = new(null, 0, null, null);

        public static FolderState NotEmpty { get; } = new(PublishStatus.FolderNotEmpty, 0, null, null);
    }

    /// <summary>
    /// Maps the encrypted byte count of one content item, capped at its listed length, onto the progress of the whole publish. A
    /// package archive or image plaintext is longer than the listed length, so without the cap progress would run ahead of the next
    /// item's offset and go backwards.
    /// </summary>
    private sealed class ByteProgress(IProgress<ContentProgress> target, long offset, long length, long total, string name) : IProgress<long>
    {
        public void Report(long value) => target.Report(new(Math.Min(total, offset + Math.Min(value, length)), total, name));
    }
}
