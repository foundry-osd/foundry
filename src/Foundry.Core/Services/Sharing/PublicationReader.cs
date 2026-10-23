// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Sharing;

/// <summary>Outcome of reading a publication folder.</summary>
public enum PublicationReadStatus
{
    /// <summary>The current manifest was verified and decrypted.</summary>
    Ok,

    /// <summary>The pointer names a manifest that has not arrived yet (eventual consistency); retry at the next check.</summary>
    NotYetAvailable,

    /// <summary>The folder is missing or could not be read.</summary>
    FolderUnavailable,

    /// <summary>The folder has no <c>current.json</c>, or it is not a publication pointer.</summary>
    NotAPublication,

    /// <summary>
    /// The password does not open the manifest. With a pinned key the signature was verified first, so this is a wrong password;
    /// without one (preview) it can also be a modified manifest, which AES-GCM cannot tell apart.
    /// </summary>
    WrongPassword,

    /// <summary>
    /// The manifest was modified, is malformed, is not a signed publication (for example an export put in its place by a password
    /// holder), or its sequence does not match the pointer.
    /// </summary>
    Tampered,

    /// <summary>The manifest is signed by a key other than the pinned one; see <see cref="PublicationReadResult.SignerKeyId"/>.</summary>
    UnknownPublisher,

    /// <summary>The pointer sequence is lower than the applied one.</summary>
    RollbackRejected,

    /// <summary>The pointer sequence equals the applied one; the manifest was not opened.</summary>
    UpToDate,

    /// <summary>The pointer, envelope, payload or configuration requires a newer Foundry version (not authenticated).</summary>
    Unsupported
}

/// <summary>Result of a publication read.</summary>
/// <param name="Status">Outcome.</param>
/// <param name="Payload">Verified payload with a migrated configuration; set only on <see cref="PublicationReadStatus.Ok"/>.</param>
/// <param name="Sequence">Sequence parsed from the pointer's manifest name; 0 when no pointer was read.</param>
/// <param name="SignerKeyId">
/// On <see cref="PublicationReadStatus.Ok"/>, the verified signer key id. On <see cref="PublicationReadStatus.UnknownPublisher"/>, the
/// key id found in the envelope, which is not authenticated: the user must confirm the new reference through a preview. Otherwise null.
/// </param>
public sealed record PublicationReadResult(PublicationReadStatus Status, ConfigurationPackagePayload? Payload, long Sequence, string? SignerKeyId);

/// <summary>
/// Reads a publication folder as a follower (spec 6.2): <c>current.json</c> first, so an unchanged publication costs one small read,
/// then the current manifest, verified against the pinned reference key before any key derivation.
/// </summary>
public static class PublicationReader
{
    /// <summary>
    /// Checks the folder for a publication newer than <paramref name="appliedSequence"/>. The sequence comes from the manifest file
    /// name in the pointer: equal returns <see cref="PublicationReadStatus.UpToDate"/> and lower
    /// <see cref="PublicationReadStatus.RollbackRejected"/>, both without opening the manifest. A higher one is read and must be a
    /// signed publication. File names are not signed, so the authenticated sequence decides: after the signature is verified and the
    /// payload decrypted, <see cref="ConfigurationPackagePayload.Sequence"/> must equal the sequence in the name (an older signed
    /// manifest copied or renamed to a higher name returns <see cref="PublicationReadStatus.Tampered"/>) and must be higher than
    /// <paramref name="appliedSequence"/> (otherwise <see cref="PublicationReadStatus.RollbackRejected"/>, or
    /// <see cref="PublicationReadStatus.UpToDate"/> when equal). A payload is returned only when both hold.
    /// </summary>
    /// <param name="folder">Publication folder.</param>
    /// <param name="password">Publication password.</param>
    /// <param name="pinnedPublicKey">Pinned reference SubjectPublicKeyInfo; null or empty accepts any consistent signer, so followers always pass it.</param>
    /// <param name="appliedSequence">Sequence currently applied on this PC, 0 when none.</param>
    /// <param name="ct">Cancels the file reads.</param>
    public static Task<PublicationReadResult> ReadAsync(string folder, string password, byte[]? pinnedPublicKey, long appliedSequence, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(password);
        return ReadCoreAsync(folder, password, pinnedPublicKey, appliedSequence, ct);
    }

    /// <summary>
    /// Reads the current publication without a pinned key or applied sequence, for the "Follow a folder" preview. The signature is
    /// verified against the publisher key in the payload, which the caller pins once the user confirms
    /// (<see cref="ConfigurationPackagePayload.Publisher"/>).
    /// </summary>
    /// <param name="folder">Publication folder.</param>
    /// <param name="password">Publication password.</param>
    /// <param name="ct">Cancels the file reads.</param>
    public static Task<PublicationReadResult> PreviewAsync(string folder, string password, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(password);
        return ReadCoreAsync(folder, password, null, null, ct);
    }

    private static async Task<PublicationReadResult> ReadCoreAsync(string folder, string password, byte[]? pinnedPublicKey, long? appliedSequence,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
        {
            return Result(PublicationReadStatus.FolderUnavailable, 0);
        }

        try
        {
            PublicationPointerRead pointer = await PublicationFolder.ReadPointerAsync(folder, cancellationToken).ConfigureAwait(false);
            switch (pointer.State)
            {
                case PublicationPointerState.Missing or PublicationPointerState.Invalid:
                    return Result(PublicationReadStatus.NotAPublication, 0);
                case PublicationPointerState.Unsupported:
                    return Result(PublicationReadStatus.Unsupported, 0);
            }

            if (appliedSequence is { } applied && pointer.Sequence <= applied)
            {
                return Result(pointer.Sequence < applied ? PublicationReadStatus.RollbackRejected : PublicationReadStatus.UpToDate, pointer.Sequence);
            }

            byte[]? manifest = await TryReadManifestAsync(PublicationFolder.ManifestPath(folder, pointer.Manifest!), cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                return Result(PublicationReadStatus.NotYetAvailable, pointer.Sequence);
            }

            return Map(ConfigurationPackageCodec.Read(manifest, password, pinnedPublicKey), pinnedPublicKey, pointer.Sequence, appliedSequence);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result(PublicationReadStatus.FolderUnavailable, 0);
        }
    }

    /// <summary>Returns the manifest bytes, or <see langword="null"/> when the file does not exist yet.</summary>
    /// <remarks>A file over the package size limit is returned as an empty array, which the codec reports as invalid.</remarks>
    private static async Task<byte[]?> TryReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
            if (stream.Length > ConfigurationPackageCodec.MaximumPackageBytes)
            {
                return [];
            }

            byte[] bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return bytes;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static PublicationReadResult Map(PackageReadResult read, byte[]? pinnedPublicKey, long sequence, long? appliedSequence)
    {
        return read.Status switch
        {
            PackageReadStatus.Ok when IsPublication(read, sequence) => CheckAppliedSequence(read, sequence, appliedSequence),
            PackageReadStatus.WrongPassword => Result(PublicationReadStatus.WrongPassword, sequence),
            PackageReadStatus.InvalidSignature when IsOtherSigner(read.SignerKeyId, pinnedPublicKey) =>
                new(PublicationReadStatus.UnknownPublisher, null, sequence, read.SignerKeyId),
            PackageReadStatus.Unsupported => Result(PublicationReadStatus.Unsupported, sequence),

            // Ok but not a signed publication of this sequence, a failed signature, a missing or inconsistent signature, or a
            // malformed package: the pointer was written by Foundry after a complete manifest, so the folder was modified.
            _ => Result(PublicationReadStatus.Tampered, sequence)
        };
    }

    /// <summary>
    /// Applies the sequence rule to the authenticated payload sequence as well as to the pointer name, so the decision never rests on
    /// an unsigned value.
    /// </summary>
    private static PublicationReadResult CheckAppliedSequence(PackageReadResult read, long sequence, long? appliedSequence)
    {
        long authenticated = read.Payload!.Sequence;
        if (appliedSequence is { } applied && authenticated <= applied)
        {
            return Result(authenticated < applied ? PublicationReadStatus.RollbackRejected : PublicationReadStatus.UpToDate, sequence);
        }

        return new(PublicationReadStatus.Ok, read.Payload, sequence, read.SignerKeyId);
    }

    /// <summary>
    /// Requires a signed publication: the codec does not check the kind, and a password holder could otherwise put an export (which
    /// is unsigned and carries no master key) in place of the manifest.
    /// </summary>
    private static bool IsPublication(PackageReadResult read, long sequence)
    {
        return read.Payload is { Kind: PackageKind.Publication, Publisher: not null, MasterKey: not null } payload
            && read.SignerKeyId is not null
            && payload.Sequence == sequence;
    }

    private static bool IsOtherSigner(string? signerKeyId, byte[]? pinnedPublicKey)
    {
        return signerKeyId is not null && pinnedPublicKey is { Length: > 0 } && signerKeyId != PublicationSigner.KeyId(pinnedPublicKey);
    }

    private static PublicationReadResult Result(PublicationReadStatus status, long sequence) => new(status, null, sequence, null);
}
