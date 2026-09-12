// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Security.Cryptography;
using Foundry.Core.Models.Profiles;
using Foundry.Utilities.Security;

namespace Foundry.Core.Services.Profiles;

/// <summary>Encrypts bounded profile revisions; callers own plaintext buffers inside imported documents.</summary>
public interface IDeploymentProfilePackageService
{
    byte[] Export(DeploymentProfileDocument profile, ReadOnlySpan<char> passphrase);
    DeploymentProfileDocument Import(ReadOnlySpan<byte> package, ReadOnlySpan<char> passphrase);
    byte[] Encrypt(DeploymentProfileDocument profile, ReadOnlySpan<byte> wrappingKey, ProfilePackagePurpose purpose, ReadOnlySpan<byte> associatedData = default);
    DeploymentProfileDocument Decrypt(ReadOnlySpan<byte> package, ReadOnlySpan<byte> wrappingKey, ProfilePackagePurpose purpose, ReadOnlySpan<byte> associatedData = default);
}

/// <summary>Uses fresh data keys and nonces, authenticated purpose/header/context, and bounded PBKDF2-SHA256 key wrapping.</summary>
public sealed class DeploymentProfilePackageService : IDeploymentProfilePackageService
{
    private const int HeaderSize = 30;
    private const int WrappedKeySize = 60;
    private const int PayloadOffset = HeaderSize + WrappedKeySize;
    private const int EncryptionOverhead = 28;
    private const int PassphraseMode = 1;
    private const int Iterations = 600_000;
    private static ReadOnlySpan<byte> Magic => "FNDRYPRF"u8;

    /// <summary>Creates an encrypted portable export. Only explicitly supplied logical secrets and assets are included.</summary>
    public byte[] Export(DeploymentProfileDocument profile, ReadOnlySpan<char> passphrase)
    {
        ValidatePassphrase(passphrase);
        byte[] header = CreateHeader(PassphraseMode);
        byte[] key = PasswordKeyDerivation.DeriveKey(passphrase, header.AsSpan(14, 16), Iterations, 32);
        try
        {
            return EncryptPayload(profile, key, header, [], true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Authenticates and validates the complete portable export before returning its owned plaintext records.</summary>
    public DeploymentProfileDocument Import(ReadOnlySpan<byte> package, ReadOnlySpan<char> passphrase)
    {
        ValidatePassphrase(passphrase);
        ValidateEnvelope(package, PassphraseMode);
        byte[] key = PasswordKeyDerivation.DeriveKey(passphrase, package.Slice(14, 16), Iterations, 32);
        try
        {
            return DecryptPayload(package, key, [], true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Protects a revision under a caller-owned 32-byte key. Local storage retains local paths; shared revisions are portable.</summary>
    public byte[] Encrypt(DeploymentProfileDocument profile, ReadOnlySpan<byte> wrappingKey, ProfilePackagePurpose purpose, ReadOnlySpan<byte> associatedData = default)
    {
        ValidateKeyAndContext(wrappingKey, purpose, associatedData);
        return EncryptPayload(profile, wrappingKey, CreateHeader((int)purpose), associatedData, purpose != ProfilePackagePurpose.LocalStorage);
    }

    /// <summary>Requires the original purpose and external revision context, protecting metadata kept outside the ciphertext.</summary>
    public DeploymentProfileDocument Decrypt(ReadOnlySpan<byte> package, ReadOnlySpan<byte> wrappingKey, ProfilePackagePurpose purpose, ReadOnlySpan<byte> associatedData = default)
    {
        ValidateKeyAndContext(wrappingKey, purpose, associatedData);
        ValidateEnvelope(package, (int)purpose);
        return DecryptPayload(package, wrappingKey, associatedData, purpose != ProfilePackagePurpose.LocalStorage);
    }

    private static byte[] EncryptPayload(DeploymentProfileDocument profile, ReadOnlySpan<byte> wrappingKey, byte[] header, ReadOnlySpan<byte> context, bool portable)
    {
        byte[] plaintext = DeploymentProfilePayload.Serialize(profile, portable);
        byte[] dataKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            byte[] package = new byte[PayloadOffset + EncryptionOverhead + plaintext.Length];
            header.CopyTo(package, 0);
            byte[] wrappingAad = CreateAssociatedData(header, context);
            EncryptBlock(dataKey, wrappingKey, package.AsSpan(HeaderSize, WrappedKeySize), wrappingAad);
            byte[] payloadAad = CreateAssociatedData(package.AsSpan(0, PayloadOffset), context);
            EncryptBlock(plaintext, dataKey, package.AsSpan(PayloadOffset), payloadAad);
            return package;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static DeploymentProfileDocument DecryptPayload(ReadOnlySpan<byte> package, ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> context, bool portable)
    {
        byte[] dataKey = new byte[32];
        byte[] plaintext = new byte[package.Length - PayloadOffset - EncryptionOverhead];
        try
        {
            byte[] wrappingAad = CreateAssociatedData(package[..HeaderSize], context);
            DecryptBlock(package.Slice(HeaderSize, WrappedKeySize), wrappingKey, dataKey, wrappingAad);
            byte[] payloadAad = CreateAssociatedData(package[..PayloadOffset], context);
            DecryptBlock(package[PayloadOffset..], dataKey, plaintext, payloadAad);
            return DeploymentProfilePayload.Deserialize(plaintext, portable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static void EncryptBlock(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, Span<byte> destination, ReadOnlySpan<byte> aad)
    {
        RandomNumberGenerator.Fill(destination[..12]);
        using AesGcm aes = new(key, 16);
        aes.Encrypt(destination[..12], plaintext, destination[28..], destination.Slice(12, 16), aad);
    }

    private static void DecryptBlock(ReadOnlySpan<byte> source, ReadOnlySpan<byte> key, Span<byte> plaintext, ReadOnlySpan<byte> aad)
    {
        using AesGcm aes = new(key, 16);
        aes.Decrypt(source[..12], source[28..], source.Slice(12, 16), plaintext, aad);
    }

    private static byte[] CreateHeader(int mode)
    {
        byte[] header = new byte[HeaderSize];
        Magic.CopyTo(header);
        header[8] = DeploymentProfileDocument.CurrentFormatVersion;
        header[9] = (byte)mode;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10, 4), mode == PassphraseMode ? Iterations : 0);
        RandomNumberGenerator.Fill(header.AsSpan(14, 16));
        return header;
    }

    private static byte[] CreateAssociatedData(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> context)
    {
        byte[] aad = new byte[prefix.Length + context.Length];
        prefix.CopyTo(aad);
        context.CopyTo(aad.AsSpan(prefix.Length));
        return aad;
    }

    private static void ValidateEnvelope(ReadOnlySpan<byte> package, int mode)
    {
        if (package.Length <= PayloadOffset + EncryptionOverhead || package.Length > DeploymentProfilePayload.MaximumPayloadBytes + PayloadOffset + EncryptionOverhead
            || !package[..8].SequenceEqual(Magic) || package[8] != DeploymentProfileDocument.CurrentFormatVersion || package[9] != mode
            || BinaryPrimitives.ReadInt32LittleEndian(package.Slice(10, 4)) != (mode == PassphraseMode ? Iterations : 0))
        {
            throw new InvalidDataException("The encrypted profile format, purpose, size or key derivation parameters are invalid.");
        }
    }

    private static void ValidatePassphrase(ReadOnlySpan<char> passphrase)
    {
        if (passphrase.Length is < 1 or > 1024)
        {
            throw new ArgumentException("The profile passphrase must contain 1 to 1024 characters.", nameof(passphrase));
        }
    }

    private static void ValidateKeyAndContext(ReadOnlySpan<byte> wrappingKey, ProfilePackagePurpose purpose, ReadOnlySpan<byte> context)
    {
        if (wrappingKey.Length != 32 || !Enum.IsDefined(purpose) || context.Length > 16 * 1024)
        {
            throw new ArgumentException("The profile key, purpose or associated context is invalid.");
        }
    }
}
