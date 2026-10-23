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
public sealed record PublicationRequest(
    string Folder,
    FoundryConfigurationDocument Configuration,
    IReadOnlyList<PackageContent> Contents,
    IReadOnlyDictionary<string, string>? Secrets,
    PublicationSigningKey Signer,
    byte[] MasterKey,
    string Password,
    Guid PublisherId,
    string PublisherName);

/// <summary>Outcome of a publication write.</summary>
public enum PublishStatus
{
    /// <summary>The manifest was written and activated (or, for a rewrap, rewritten under the new password).</summary>
    Published,

    /// <summary>The folder holds a publication signed by another key; nothing was written.</summary>
    ForeignPublisher,

    /// <summary>
    /// The folder holds files that are not a publication this PC can continue: unrelated files, a pointer that is malformed, newer
    /// or leads to a missing or unsigned manifest, or (for a rewrap) a current manifest that does not verify. Nothing was written.
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
/// <param name="Sequence">The new sequence when published; the current folder sequence for <see cref="PublishStatus.ForeignPublisher"/>; otherwise 0.</param>
/// <param name="ForeignKeyId">Key id of the current folder signer, set for <see cref="PublishStatus.ForeignPublisher"/>; read without the password, so not authenticated.</param>
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
    /// Publishes a new version. An empty folder starts at sequence 1; a folder whose current manifest is signed by
    /// <see cref="PublicationRequest.Signer"/> continues at its sequence + 1 (the signer is read from the envelope, no password
    /// needed). Content is keyed by hash only: a hash listed several times (for example as a package and a driver set) is written
    /// once, and a content file already complete in the folder is not rewritten.
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

            long sequence = state.Sequence + 1;
            await WriteContentsAsync(request, contents, progress, cancellationToken).ConfigureAwait(false);
            string manifest = PublicationFolder.NewManifest(sequence);
            byte[] manifestBytes = ConfigurationPackageCodec.Write(CreatePayload(request, sequence), request.Password, request.Signer);
            cancellationToken.ThrowIfCancellationRequested();
            await PublicationFolder.WriteAtomicAsync(PublicationFolder.ManifestPath(request.Folder, manifest), manifestBytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await PublicationFolder.WritePointerAsync(request.Folder, manifest, CancellationToken.None).ConfigureAwait(false);
            try
            {
                RemoveUnreferenced(request, manifest, state);
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
    /// <see cref="PublishStatus.FolderNotEmpty"/> or <see cref="PublishStatus.FolderUnavailable"/>.
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

            if (state.Manifest is null)
            {
                return PublishStatus.FolderUnavailable;
            }

            PackageReadResult read = ConfigurationPackageCodec.Read(state.ManifestBytes, oldPassword, signer.PublicKey);
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
            await PublicationFolder.WriteAtomicAsync(PublicationFolder.ManifestPath(folder, state.Manifest), rewrapped, cancellationToken).ConfigureAwait(false);
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
    }

    private static void ValidatePassword(string password)
    {
        if (PassphrasePolicy.Validate(password) != PassphraseValidation.Valid)
        {
            throw new ArgumentException("The publication password does not satisfy the passphrase policy.", nameof(password));
        }
    }

    /// <summary>Decides from <c>current.json</c> and the signer of its manifest whether this key may continue the folder.</summary>
    private static async Task<FolderState> InspectAsync(string folder, string keyId, CancellationToken cancellationToken)
    {
        PublicationPointerRead pointer = await PublicationFolder.ReadPointerAsync(folder, cancellationToken).ConfigureAwait(false);
        if (pointer.State == PublicationPointerState.Missing)
        {
            return PublicationFolder.HoldsOnlyPublicationFiles(folder) ? FolderState.Empty : FolderState.NotEmpty;
        }

        string manifestPath = pointer.State == PublicationPointerState.Ok ? PublicationFolder.ManifestPath(folder, pointer.Manifest!) : string.Empty;
        if (pointer.State != PublicationPointerState.Ok || !File.Exists(manifestPath) || new FileInfo(manifestPath).Length > ConfigurationPackageCodec.MaximumPackageBytes)
        {
            return FolderState.NotEmpty;
        }

        byte[] manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        string? signerKeyId = ConfigurationPackageCodec.ReadSignerKeyId(manifestBytes);
        if (signerKeyId is null)
        {
            return FolderState.NotEmpty;
        }

        return signerKeyId == keyId
            ? new FolderState(null, pointer.Sequence, null, pointer.Manifest, manifestBytes)
            : new FolderState(PublishStatus.ForeignPublisher, pointer.Sequence, signerKeyId, null, []);
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
            if (!ContentTransfer.IsComplete(path))
            {
                byte[] key = ContentTransfer.DeriveContentKey(request.MasterKey, content.Hash);
                try
                {
                    await using Stream stream = await source.OpenAsync(content, cancellationToken).ConfigureAwait(false);
                    IProgress<long>? bytes = progress is null ? null : new ByteProgress(progress, done, total, content.Name);
                    await ContentTransfer.EncryptAsync(stream, path, key, bytes, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
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
    /// After activation, keeps the new and the previous manifest and deletes every other manifest, then deletes content files neither
    /// kept manifest lists. The previous manifest's list is read with the request password; when that fails no content is deleted.
    /// A file that cannot be deleted (for example while a follower reads it) is left for the next publish.
    /// </summary>
    private static void RemoveUnreferenced(PublicationRequest request, string manifest, FolderState previous)
    {
        HashSet<string> keptManifests = new(StringComparer.OrdinalIgnoreCase) { PublicationFolder.ManifestFileName(manifest) };
        HashSet<string> referenced = new(request.Contents.Select(content => content.Hash), StringComparer.Ordinal);
        bool referencesKnown = true;
        if (previous.Manifest is not null)
        {
            keptManifests.Add(PublicationFolder.ManifestFileName(previous.Manifest));
            PackageReadResult read = ConfigurationPackageCodec.Read(previous.ManifestBytes, request.Password, request.Signer.PublicKey);
            referencesKnown = read.Status == PackageReadStatus.Ok;
            if (referencesKnown)
            {
                referenced.UnionWith(read.Payload!.Contents.Select(content => content.Hash));
            }
        }

        DeleteFiles(PublicationFolder.ManifestsDirectory(request.Folder), name => !keptManifests.Contains(name));
        if (referencesKnown)
        {
            DeleteFiles(PublicationFolder.ContentDirectory(request.Folder), name => PublicationFolder.ContentHashOf(name) is { } hash && !referenced.Contains(hash));
        }
    }

    private static void DeleteFiles(string directory, Func<string, bool> shouldDelete)
    {
        foreach (string path in Directory.EnumerateFiles(directory))
        {
            if (!shouldDelete(Path.GetFileName(path)))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A follower may be reading the file; the next publish deletes it.
            }
        }
    }

    /// <summary>What <see cref="InspectAsync"/> found.</summary>
    /// <param name="Status">Set when the folder cannot be continued by this key.</param>
    /// <param name="Sequence">Sequence of the current manifest, 0 for an empty folder.</param>
    /// <param name="ForeignKeyId">Signer of the current manifest when it is another key.</param>
    /// <param name="Manifest">Pointer path of the current manifest of this key.</param>
    /// <param name="ManifestBytes">Envelope bytes of <paramref name="Manifest"/>.</param>
    private sealed record FolderState(PublishStatus? Status, long Sequence, string? ForeignKeyId, string? Manifest, byte[] ManifestBytes)
    {
        public static FolderState Empty { get; } = new(null, 0, null, null, []);

        public static FolderState NotEmpty { get; } = new(PublishStatus.FolderNotEmpty, 0, null, null, []);
    }

    /// <summary>Maps the encrypted byte count of one content item onto the progress of the whole publish.</summary>
    private sealed class ByteProgress(IProgress<ContentProgress> target, long offset, long total, string name) : IProgress<long>
    {
        public void Report(long value) => target.Report(new(Math.Min(total, offset + value), total, name));
    }
}
