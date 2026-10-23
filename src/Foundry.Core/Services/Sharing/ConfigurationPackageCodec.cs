// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Utilities.Security;

namespace Foundry.Core.Services.Sharing;

/// <summary>Outcome of reading a configuration package.</summary>
public enum PackageReadStatus
{
    /// <summary>The package was decrypted, verified when signed, and its configuration migrated.</summary>
    Ok,

    /// <summary>
    /// Decryption failed. Either the passphrase is wrong or a package without a verified signature was modified:
    /// AES-GCM has one authentication tag, so the two cannot be told apart.
    /// </summary>
    WrongPassword,

    /// <summary>A signature made with the expected (or embedded) key does not cover the package as found: it was modified after signing.</summary>
    Tampered,

    /// <summary>A required signature is missing, made with another key, or does not match the publisher in the payload.</summary>
    InvalidSignature,

    /// <summary>
    /// The envelope, payload or configuration requires a newer Foundry version. An envelope version is read before any
    /// signature check, so on a followed folder this status is not authenticated either.
    /// </summary>
    Unsupported,

    /// <summary>The bytes are not a well-formed configuration package, or its parameters are out of range.</summary>
    Invalid
}

/// <summary>Result of <see cref="ConfigurationPackageCodec.Read"/>.</summary>
/// <param name="Status">Read outcome.</param>
/// <param name="Payload">Decrypted payload with a migrated configuration; set only when <paramref name="Status"/> is <see cref="PackageReadStatus.Ok"/>.</param>
/// <param name="SignerKeyId">
/// Key id found in the envelope signature, when one is present, even when the read fails. It is authenticated only on
/// <see cref="PackageReadStatus.Ok"/>; otherwise it is copied from the unverified envelope, so callers must not present it as a
/// confirmed publisher change.
/// </param>
public sealed record PackageReadResult(PackageReadStatus Status, ConfigurationPackagePayload? Payload, string? SignerKeyId);

/// <summary>
/// Writes and reads the encrypted configuration package used by export files and publication manifests (spec 5.3):
/// a JSON envelope carrying a PBKDF2-SHA256 / AES-256-GCM encrypted JSON payload, signed with ECDSA P-256 on publications.
/// </summary>
public static class ConfigurationPackageCodec
{
    /// <summary>Envelope <c>format</c> value.</summary>
    internal const string Format = "foundry-configuration";

    /// <summary>Envelope version written by this Foundry version; readers accept 1 to this value.</summary>
    internal const int CurrentVersion = 1;

    /// <summary>PBKDF2 iterations used by the writer.</summary>
    internal const int WriteIterations = 600_000;

    /// <summary>Lowest PBKDF2 iteration count a reader accepts.</summary>
    internal const int MinimumIterations = 600_000;

    /// <summary>Highest PBKDF2 iteration count a reader accepts, bounding the work a hostile file can cause.</summary>
    internal const int MaximumIterations = 10_000_000;

    /// <summary>Largest package a reader accepts (exports embed at most 16 MiB of files, base64 encoded twice).</summary>
    internal const int MaximumPackageBytes = 64 * 1024 * 1024;

    private const string KdfName = "PBKDF2-SHA256";
    private const string CipherName = "AES-256-GCM";
    private const string SignatureAlgorithm = "ECDSA-P256-SHA256";
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <summary>Largest total of embedded files an export may carry.</summary>
    internal const int MaximumEmbeddedFileBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Frozen envelope options, deliberately separate from <see cref="ConfigurationPackageJson.SerializerOptions"/>: the signed
    /// bytes depend on them, so payload or publication-file settings must never change them.
    /// </summary>
    private static readonly JsonSerializerOptions EnvelopeSerializerOptions = CreateEnvelopeSerializerOptions();

    /// <summary>Encrypts a payload under a passphrase and, for a publication, signs the envelope.</summary>
    /// <param name="payload">Payload to write; a publication needs <paramref name="signer"/> and a matching <see cref="ConfigurationPackagePayload.Publisher"/>.</param>
    /// <param name="passphrase">Passphrase that must satisfy <see cref="PassphrasePolicy"/>.</param>
    /// <param name="signer">Reference signing key; required for publications and refused for exports.</param>
    /// <returns>UTF-8 JSON envelope bytes.</returns>
    /// <exception cref="ArgumentException">
    /// The passphrase breaks the policy, the kind, signer and publisher do not agree, embedded files exceed 16 MiB in total,
    /// or the package would exceed 64 MiB. The current payload version is always written, whatever the caller set.
    /// </exception>
    public static byte[] Write(ConfigurationPackagePayload payload, ReadOnlySpan<char> passphrase, PublicationSigningKey? signer = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (PassphrasePolicy.Validate(passphrase) != PassphraseValidation.Valid)
        {
            throw new ArgumentException("The package passphrase does not satisfy the passphrase policy.", nameof(passphrase));
        }

        ValidateSigner(payload, signer);
        if (payload.Files is not null && payload.Files.Values.Sum(file => (long)(file?.Length ?? 0)) > MaximumEmbeddedFileBytes)
        {
            throw new ArgumentException("The embedded files exceed the export size limit.", nameof(payload));
        }

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(
            payload with { PayloadVersion = ConfigurationPackagePayload.CurrentPayloadVersion },
            ConfigurationPackageJson.SerializerOptions);
        try
        {
            byte[] package = Seal(plaintext, passphrase, signer);
            return package.Length <= MaximumPackageBytes
                ? package
                : throw new ArgumentException("The package exceeds the maximum package size.", nameof(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Reads a package. When <paramref name="expectedPublicKey"/> is set (a follower's pinned reference key), the envelope must
    /// carry a signature with that key's id, verified before decryption. Without it, a signature present is verified after
    /// decryption against the publisher key in the payload. An unsigned publication is never accepted.
    /// Read does not check <see cref="ConfigurationPackagePayload.Kind"/>: callers must check it (for example, a follower
    /// requires <see cref="PackageKind.Publication"/>).
    /// </summary>
    /// <param name="package">Envelope bytes.</param>
    /// <param name="passphrase">Passphrase to try; not checked against the policy.</param>
    /// <param name="expectedPublicKey">Pinned SubjectPublicKeyInfo, or empty to accept any consistent signer.</param>
    /// <returns>
    /// The status, the payload (only on <see cref="PackageReadStatus.Ok"/>) and the signer key id found in the envelope.
    /// Never throws for malformed input.
    /// </returns>
    public static PackageReadResult Read(ReadOnlySpan<byte> package, ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> expectedPublicKey = default)
    {
        PackageReadStatus? headerStatus = TryReadEnvelope(package, out Envelope? envelope);
        if (headerStatus is not null)
        {
            return new PackageReadResult(headerStatus.Value, null, null);
        }

        string? signerKeyId = envelope!.Signature?.KeyId;
        if (!expectedPublicKey.IsEmpty)
        {
            PackageReadStatus? signatureStatus = CheckExpectedSignature(envelope, expectedPublicKey);
            if (signatureStatus is not null)
            {
                return new PackageReadResult(signatureStatus.Value, null, signerKeyId);
            }
        }

        byte[]? plaintext = TryDecrypt(envelope, passphrase);
        if (plaintext is null)
        {
            return new PackageReadResult(PackageReadStatus.WrongPassword, null, signerKeyId);
        }

        try
        {
            PackageReadStatus? payloadStatus = TryReadPayload(plaintext, out ConfigurationPackagePayload? payload);
            payloadStatus ??= CheckPayloadSigner(envelope, payload!, verify: expectedPublicKey.IsEmpty);
            return payloadStatus is null
                ? new PackageReadResult(PackageReadStatus.Ok, payload, signerKeyId)
                : new PackageReadResult(payloadStatus.Value, null, signerKeyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Returns whether the bytes are a configuration package envelope (any version), as opposed to a legacy profile or another file.</summary>
    public static bool IsPackage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumPackageBytes || !TryParseJson(bytes, out JsonDocument? document))
        {
            return false;
        }

        using (document)
        {
            return HasFormat(document!.RootElement);
        }
    }

    /// <summary>
    /// Returns the signature key id of a well-formed envelope of a supported version without the passphrase, or
    /// <see langword="null"/> when the bytes are not such an envelope or carry no signature. The value is not authenticated.
    /// </summary>
    internal static string? ReadSignerKeyId(ReadOnlySpan<byte> package)
    {
        return TryReadEnvelope(package, out Envelope? envelope) is null ? envelope!.Signature?.KeyId : null;
    }

    /// <summary>Encrypts already serialized payload JSON; <see cref="Write"/> validates the payload and passphrase first.</summary>
    internal static byte[] Seal(ReadOnlySpan<byte> payloadJson, ReadOnlySpan<char> passphrase, PublicationSigningKey? signer)
    {
        byte[] salt = PasswordKeyDerivation.GenerateSalt();
        KdfParameters kdf = new(KdfName, WriteIterations, salt);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] tag = new byte[TagSizeBytes];
        byte[] ciphertext = new byte[payloadJson.Length];
        byte[] key = PasswordKeyDerivation.DeriveKey(passphrase, salt, WriteIterations, KeySizeBytes);
        try
        {
            using AesGcm aes = new(key, TagSizeBytes);
            aes.Encrypt(nonce, payloadJson, ciphertext, tag, CreateAssociatedData(Format, CurrentVersion, kdf, CipherName));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        Envelope envelope = new(Format, CurrentVersion, kdf, CipherName, nonce, tag, ciphertext, null);
        if (signer is not null)
        {
            envelope = envelope with { Signature = new EnvelopeSignature(SignatureAlgorithm, signer.KeyId, signer.Sign(GetSignedBytes(envelope))) };
        }

        return JsonSerializer.SerializeToUtf8Bytes(envelope, EnvelopeSerializerOptions);
    }

    private static void ValidateSigner(ConfigurationPackagePayload payload, PublicationSigningKey? signer)
    {
        if ((payload.Kind == PackageKind.Publication) != (signer is not null))
        {
            throw new ArgumentException("A publication must be signed and an export must not be.", nameof(signer));
        }

        if (signer is not null
            && (payload.Publisher is null
                || payload.Publisher.KeyId != signer.KeyId
                || !payload.Publisher.PublicKey.AsSpan().SequenceEqual(signer.PublicKey)))
        {
            throw new ArgumentException("The publication publisher does not match the signing key.", nameof(payload));
        }
    }

    private static PackageReadStatus? TryReadEnvelope(ReadOnlySpan<byte> package, out Envelope? envelope)
    {
        envelope = null;
        if (package.Length > MaximumPackageBytes || !TryParseJson(package, out JsonDocument? document))
        {
            return PackageReadStatus.Invalid;
        }

        using (document)
        {
            JsonElement root = document!.RootElement;
            if (!HasFormat(root) || !root.TryGetProperty("version", out JsonElement version) || !version.TryGetInt32(out int versionNumber) || versionNumber < 1)
            {
                return PackageReadStatus.Invalid;
            }

            if (versionNumber > CurrentVersion)
            {
                return PackageReadStatus.Unsupported;
            }

            try
            {
                envelope = root.Deserialize<Envelope>(EnvelopeSerializerOptions);
            }
            catch (JsonException)
            {
                return PackageReadStatus.Invalid;
            }

            return IsWellFormed(envelope) ? null : PackageReadStatus.Invalid;
        }
    }

    private static bool IsWellFormed(Envelope? envelope)
    {
        return envelope is { Kdf: { Name: KdfName, Salt.Length: PasswordKeyDerivation.SaltSizeBytes } kdf, Cipher: CipherName, Nonce.Length: NonceSizeBytes, Tag.Length: TagSizeBytes, Ciphertext: not null }
            && kdf.Iterations is >= MinimumIterations and <= MaximumIterations
            && envelope.Signature is null or { Algorithm: SignatureAlgorithm, KeyId: not null, Value: not null };
    }

    private static PackageReadStatus? CheckExpectedSignature(Envelope envelope, ReadOnlySpan<byte> expectedPublicKey)
    {
        if (envelope.Signature is null || envelope.Signature.KeyId != PublicationSigner.KeyId(expectedPublicKey))
        {
            return PackageReadStatus.InvalidSignature;
        }

        return PublicationSigner.Verify(expectedPublicKey, GetSignedBytes(envelope), envelope.Signature.Value) ? null : PackageReadStatus.Tampered;
    }

    private static PackageReadStatus? CheckPayloadSigner(Envelope envelope, ConfigurationPackagePayload payload, bool verify)
    {
        if (envelope.Signature is null)
        {
            return payload.Kind == PackageKind.Publication ? PackageReadStatus.InvalidSignature : null;
        }

        PackagePublisher? publisher = payload.Publisher;
        if (publisher?.PublicKey is null
            || publisher.KeyId != envelope.Signature.KeyId
            || PublicationSigner.KeyId(publisher.PublicKey) != envelope.Signature.KeyId)
        {
            return PackageReadStatus.InvalidSignature;
        }

        return !verify || PublicationSigner.Verify(publisher.PublicKey, GetSignedBytes(envelope), envelope.Signature.Value) ? null : PackageReadStatus.Tampered;
    }

    private static byte[]? TryDecrypt(Envelope envelope, ReadOnlySpan<char> passphrase)
    {
        byte[] plaintext = new byte[envelope.Ciphertext.Length];
        byte[] key = PasswordKeyDerivation.DeriveKey(passphrase, envelope.Kdf.Salt, envelope.Kdf.Iterations, KeySizeBytes);
        try
        {
            using AesGcm aes = new(key, TagSizeBytes);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, CreateAssociatedData(envelope.Format, envelope.Version, envelope.Kdf, envelope.Cipher));
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static PackageReadStatus? TryReadPayload(byte[] plaintext, out ConfigurationPackagePayload? payload)
    {
        payload = null;
        if (!TryParseJson(plaintext, out JsonDocument? document))
        {
            return PackageReadStatus.Invalid;
        }

        using (document)
        {
            JsonElement root = document!.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("payloadVersion", out JsonElement payloadVersion) || !payloadVersion.TryGetInt32(out int payloadVersionNumber) || payloadVersionNumber < 1
                || !root.TryGetProperty("configuration", out JsonElement configuration) || configuration.ValueKind != JsonValueKind.Object
                || !configuration.TryGetProperty("schemaVersion", out JsonElement schemaVersion) || !schemaVersion.TryGetInt32(out int schemaVersionNumber)
                || HasNullMember(configuration))
            {
                return PackageReadStatus.Invalid;
            }

            if (payloadVersionNumber > ConfigurationPackagePayload.CurrentPayloadVersion || schemaVersionNumber > ConfigurationSchemaVersions.FoundryCurrent)
            {
                return PackageReadStatus.Unsupported;
            }

            try
            {
                ConfigurationPackagePayload? read = root.Deserialize<ConfigurationPackagePayload>(ConfigurationPackageJson.SerializerOptions);
                if (read is null || read.Contents is null || read.Contents.Any(content => content is null))
                {
                    return PackageReadStatus.Invalid;
                }

                payload = read with { Configuration = FoundryConfigurationMigration.ApplySchemaMigrations(read.Configuration) };
                return null;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            {
                return PackageReadStatus.Invalid;
            }
        }
    }

    /// <summary>
    /// System.Text.Json assigns an explicit <c>null</c> to non-nullable configuration sections, which migration and later
    /// consumers do not expect. Writers omit null members, so any null top-level section marks a hand-made package.
    /// </summary>
    private static bool HasNullMember(JsonElement configuration)
    {
        foreach (JsonProperty section in configuration.EnumerateObject())
        {
            if (section.Value.ValueKind == JsonValueKind.Null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseJson(ReadOnlySpan<byte> bytes, out JsonDocument? document)
    {
        document = null;
        Utf8JsonReader reader = new(bytes, new JsonReaderOptions { MaxDepth = ConfigurationPackageJson.MaxDepth });
        try
        {
            document = JsonDocument.ParseValue(ref reader);
            if (reader.Read())
            {
                document.Dispose();
                document = null;
            }
        }
        catch (JsonException)
        {
            document?.Dispose();
            document = null;
        }

        return document is not null;
    }

    private static bool HasFormat(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("format", out JsonElement format)
            && format.ValueKind == JsonValueKind.String
            && format.ValueEquals(Format);
    }

    /// <summary>
    /// Returns the bytes a publication signature covers: the envelope serialized with <see cref="EnvelopeSerializerOptions"/>
    /// without its <c>signature</c> member. Part of the on-disk contract: changing it invalidates every published signature.
    /// </summary>
    internal static byte[] GetSignedBytes(Envelope envelope)
    {
        return JsonSerializer.SerializeToUtf8Bytes(envelope with { Signature = null }, EnvelopeSerializerOptions);
    }

    /// <summary>
    /// Returns the AES-GCM associated data: UTF-8 of <c>format|version|kdf.name|kdf.iterations|kdf.salt(base64)|cipher</c>.
    /// Part of the on-disk contract: changing it makes every existing package unreadable.
    /// </summary>
    internal static byte[] CreateAssociatedData(string format, int version, KdfParameters kdf, string cipher)
    {
        return Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{format}|{version}|{kdf.Name}|{kdf.Iterations}|{Convert.ToBase64String(kdf.Salt)}|{cipher}"));
    }

    private static JsonSerializerOptions CreateEnvelopeSerializerOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = ConfigurationPackageJson.MaxDepth
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Envelope JSON (spec 5.3); binary members are base64.</summary>
    internal sealed record Envelope(
        string Format,
        int Version,
        KdfParameters Kdf,
        string Cipher,
        byte[] Nonce,
        byte[] Tag,
        byte[] Ciphertext,
        EnvelopeSignature? Signature);

    /// <summary>Envelope <c>kdf</c> member.</summary>
    internal sealed record KdfParameters(string Name, int Iterations, byte[] Salt);

    /// <summary>Envelope <c>signature</c> member, present on publications only.</summary>
    internal sealed record EnvelopeSignature(string Algorithm, string KeyId, byte[] Value);
}
