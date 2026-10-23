// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Foundry.Core.Services.Configuration.Legacy;

/// <summary>
/// Frozen, decrypt-only copy of the <c>FNDRYPRF</c> version 1 envelope written by the retired profile feature.
/// </summary>
/// <remarks>
/// Layout: a 30-byte header (magic, format version, mode, PBKDF2 iterations as little-endian int32, 16-byte salt), the
/// 32-byte data key wrapped with AES-256-GCM (nonce 12, tag 16, ciphertext), then the AES-256-GCM payload. The wrapped
/// key authenticates the header plus the external context; the payload authenticates the header, the wrapped key and
/// the context. Passphrase packages derive the wrapping key with PBKDF2-SHA256 (600,000 iterations) and have no context;
/// local revisions use a Credential Manager key and the head descriptor as context.
/// </remarks>
internal static class LegacyPackageEnvelope
{
    private const int HeaderSize = 30;
    private const int WrappedKeySize = 60;
    private const int PayloadOffset = HeaderSize + WrappedKeySize;
    private const int EncryptionOverhead = 28;
    private const int MaximumPayloadBytes = 16 * 1024 * 1024;
    private const byte FormatVersion = 1;
    private const byte PassphraseMode = 1;
    private const byte LocalStorageMode = 2;
    private const int PassphraseIterations = 600_000;

    /// <summary>The largest envelope the retired feature could write.</summary>
    internal const int MaximumPackageBytes = MaximumPayloadBytes + PayloadOffset + EncryptionOverhead;

    private static ReadOnlySpan<byte> Magic => "FNDRYPRF"u8;

    internal static bool HasMagic(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Magic);

    /// <summary>Returns the owned plaintext payload of an exported package or connection file.</summary>
    /// <exception cref="LegacyPasswordException">The passphrase does not unwrap the data key.</exception>
    /// <exception cref="InvalidDataException">The envelope is malformed or its payload fails authentication.</exception>
    internal static byte[] DecryptWithPassphrase(ReadOnlySpan<byte> package, ReadOnlySpan<char> passphrase)
    {
        ValidateHeader(package, PassphraseMode, PassphraseIterations);
        if (passphrase.Length is < 1 or > 1024)
        {
            throw new LegacyPasswordException();
        }
        byte[] key = new byte[32];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(passphrase, package.Slice(14, 16), key, PassphraseIterations, HashAlgorithmName.SHA256);
            return Decrypt(package, key, [], passphraseMode: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Returns the owned plaintext payload of a local revision protected by its Credential Manager key.</summary>
    /// <exception cref="InvalidDataException">The envelope is malformed, the key does not match, or the context was altered.</exception>
    internal static byte[] DecryptLocalStorage(ReadOnlySpan<byte> package, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData)
    {
        ValidateHeader(package, LocalStorageMode, 0);
        if (key.Length != 32)
        {
            throw new InvalidDataException("The legacy local profile key is invalid.");
        }
        return Decrypt(package, key, associatedData, passphraseMode: false);
    }

    private static byte[] Decrypt(ReadOnlySpan<byte> package, ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> context, bool passphraseMode)
    {
        byte[] dataKey = new byte[32];
        byte[] plaintext = new byte[package.Length - PayloadOffset - EncryptionOverhead];
        try
        {
            if (!TryDecryptBlock(package.Slice(HeaderSize, WrappedKeySize), wrappingKey, dataKey, Concat(package[..HeaderSize], context)))
            {
                throw passphraseMode ? new LegacyPasswordException() : new InvalidDataException("The legacy local profile key does not open its revision.");
            }
            if (!TryDecryptBlock(package[PayloadOffset..], dataKey, plaintext, Concat(package[..PayloadOffset], context)))
            {
                throw new InvalidDataException("The legacy profile payload failed authentication.");
            }
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    private static bool TryDecryptBlock(ReadOnlySpan<byte> source, ReadOnlySpan<byte> key, Span<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        using AesGcm aes = new(key, 16);
        try
        {
            aes.Decrypt(source[..12], source[28..], source.Slice(12, 16), plaintext, associatedData);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
    }

    private static byte[] Concat(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> context)
    {
        byte[] result = new byte[prefix.Length + context.Length];
        prefix.CopyTo(result);
        context.CopyTo(result.AsSpan(prefix.Length));
        return result;
    }

    private static void ValidateHeader(ReadOnlySpan<byte> package, byte mode, int iterations)
    {
        if (package.Length < PayloadOffset + EncryptionOverhead + 1 || package.Length > MaximumPackageBytes)
            throw new InvalidDataException($"The legacy profile size is invalid. Size={package.Length}.");
        if (!HasMagic(package))
            throw new InvalidDataException("The legacy profile signature is invalid.");
        if (package[8] != FormatVersion)
            throw new InvalidDataException($"The legacy profile format version is unsupported. Version={package[8]}.");
        if (package[9] != mode)
            throw new InvalidDataException($"The legacy profile purpose is invalid for this operation. Purpose={package[9]}, ExpectedPurpose={mode}.");
        if (BinaryPrimitives.ReadInt32LittleEndian(package.Slice(10, 4)) != iterations)
            throw new InvalidDataException("The legacy profile key derivation parameters are invalid.");
    }
}
