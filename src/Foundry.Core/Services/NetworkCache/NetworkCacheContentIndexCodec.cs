// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Writes and reads the signed content index: a JSON envelope <c>{ "payload": base64, "signature": base64 }</c> whose
/// signature is ECDSA P-256 SHA-256 (IEEE P1363) over the payload bytes, so the payload is verified exactly as written.
/// </summary>
public static class NetworkCacheContentIndexCodec
{
    /// <summary>Largest index file a reader accepts and a writer produces.</summary>
    public const int MaximumBytes = 4 * 1024 * 1024;

    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions EnvelopeSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Serializes and signs an index.</summary>
    /// <param name="index">Index to write; its <see cref="NetworkCacheContentIndex.KeyId"/> must be the signer's.</param>
    /// <param name="signer">Key that signs the payload.</param>
    /// <returns>UTF-8 JSON envelope bytes.</returns>
    /// <exception cref="ArgumentException">The index key id differs from the signer's, or the file would exceed <see cref="MaximumBytes"/>.</exception>
    public static byte[] Write(NetworkCacheContentIndex index, PublicationSigningKey signer)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(signer);
        if (!string.Equals(index.KeyId, signer.KeyId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The index key id does not match the signing key.", nameof(index));
        }

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(index, ConfigurationJsonDefaults.SerializerOptions);
        byte[] file = JsonSerializer.SerializeToUtf8Bytes(new Envelope(payload, signer.Sign(payload)), EnvelopeSerializerOptions);
        return file.Length <= MaximumBytes
            ? file
            : throw new ArgumentException("The index exceeds the maximum index size.", nameof(index));
    }

    /// <summary>
    /// Reads an index and authenticates it against the trusted key. Checks run in order: size, JSON, signature, key id,
    /// schema version, then sequence; the first failure decides the status. Never throws for malformed input.
    /// </summary>
    /// <param name="file">Index file bytes.</param>
    /// <param name="trustedPublicKey">Trusted SubjectPublicKeyInfo: the PC's own key, or the key pinned by a follower.</param>
    /// <param name="minimumSequence">Lowest sequence accepted; a lower one is reported as <see cref="NetworkCacheIndexReadStatus.TooOld"/>.</param>
    /// <param name="index">The index on <see cref="NetworkCacheIndexReadStatus.Valid"/>; otherwise <see langword="null"/>.</param>
    public static NetworkCacheIndexReadStatus Read(
        ReadOnlySpan<byte> file,
        ReadOnlySpan<byte> trustedPublicKey,
        long minimumSequence,
        out NetworkCacheContentIndex? index)
    {
        index = null;
        if (file.Length > MaximumBytes)
        {
            return NetworkCacheIndexReadStatus.TooLarge;
        }

        if (!TryReadEnvelope(file, out Envelope? envelope) || !TryReadPayload(envelope!.Payload, out NetworkCacheContentIndex? payload))
        {
            return NetworkCacheIndexReadStatus.Malformed;
        }

        if (!PublicationSigner.Verify(trustedPublicKey, envelope.Payload, envelope.Signature))
        {
            return NetworkCacheIndexReadStatus.SignatureInvalid;
        }

        if (!string.Equals(payload!.KeyId, PublicationSigner.KeyId(trustedPublicKey), StringComparison.Ordinal))
        {
            return NetworkCacheIndexReadStatus.UnexpectedKey;
        }

        if (payload.SchemaVersion != SupportedSchemaVersion)
        {
            return NetworkCacheIndexReadStatus.UnsupportedSchema;
        }

        if (payload.Sequence < minimumSequence)
        {
            return NetworkCacheIndexReadStatus.TooOld;
        }

        index = payload;
        return NetworkCacheIndexReadStatus.Valid;
    }

    /// <summary>
    /// Returns the payload without checking the signature or the key, or <see langword="null"/> when the file is too large or
    /// malformed. For diagnostics and for locating content only: the result must never drive a deployment.
    /// </summary>
    public static NetworkCacheContentIndex? ReadUnverified(ReadOnlySpan<byte> file)
    {
        if (file.Length > MaximumBytes || !TryReadEnvelope(file, out Envelope? envelope))
        {
            return null;
        }

        return TryReadPayload(envelope!.Payload, out NetworkCacheContentIndex? payload) ? payload : null;
    }

    private static bool TryReadEnvelope(ReadOnlySpan<byte> file, out Envelope? envelope)
    {
        envelope = null;
        try
        {
            Envelope? read = JsonSerializer.Deserialize<Envelope>(file, EnvelopeSerializerOptions);
            if (read is { Payload: not null, Signature: not null })
            {
                envelope = read;
            }
        }
        catch (JsonException)
        {
        }

        return envelope is not null;
    }

    /// <summary>
    /// System.Text.Json assigns an explicit <c>null</c> to non-nullable members, so a section or list that deserializes
    /// as <see langword="null"/> marks a damaged or hand-made payload.
    /// </summary>
    private static bool TryReadPayload(byte[] payloadBytes, out NetworkCacheContentIndex? payload)
    {
        payload = null;
        try
        {
            NetworkCacheContentIndex? read = JsonSerializer.Deserialize<NetworkCacheContentIndex>(payloadBytes, ConfigurationJsonDefaults.SerializerOptions);
            if (read is { KeyId: not null, CustomImages: not null, CustomDrivers.Sets: not null, PreOobe.Actions: not null }
                && !read.CustomDrivers.Sets.Contains(null!)
                && !read.PreOobe.Actions.Contains(null!))
            {
                payload = read;
            }
        }
        catch (JsonException)
        {
        }

        return payload is not null;
    }

    /// <summary>Envelope JSON; binary members are base64.</summary>
    private sealed record Envelope(byte[] Payload, byte[] Signature);
}
