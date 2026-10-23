// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// The ECDSA P-256 private key a reference PC signs its publication manifests with. The caller owns persistence
/// (the PKCS#8 export goes to the secret vault) and disposal.
/// </summary>
public sealed class PublicationSigningKey : IDisposable
{
    private readonly ECDsa _key;
    private readonly byte[] _publicKey;

    private PublicationSigningKey(ECDsa key)
    {
        _key = key;
        _publicKey = key.ExportSubjectPublicKeyInfo();
        KeyId = PublicationSigner.KeyId(_publicKey);
    }

    /// <summary>Gets a copy of the public key as a DER SubjectPublicKeyInfo.</summary>
    public byte[] PublicKey => (byte[])_publicKey.Clone();

    /// <summary>Gets the fingerprint of <see cref="PublicKey"/>, as computed by <see cref="PublicationSigner.KeyId"/>.</summary>
    public string KeyId { get; }

    /// <summary>Creates a new random P-256 key.</summary>
    public static PublicationSigningKey Create()
    {
        return new PublicationSigningKey(ECDsa.Create(ECCurve.NamedCurves.nistP256));
    }

    /// <summary>Imports a key exported with <see cref="ExportPkcs8"/>.</summary>
    /// <exception cref="CryptographicException">The bytes are not exactly one unencrypted PKCS#8 P-256 private key.</exception>
    public static PublicationSigningKey Import(ReadOnlySpan<byte> pkcs8)
    {
        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(pkcs8, out int bytesRead);
            if (bytesRead != pkcs8.Length || !PublicationSigner.IsP256(key))
            {
                throw new CryptographicException("The publication signing key is not a P-256 PKCS#8 private key.");
            }

            return new PublicationSigningKey(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Exports the private key as unencrypted PKCS#8; the caller must store it protected.</summary>
    public byte[] ExportPkcs8()
    {
        return _key.ExportPkcs8PrivateKey();
    }

    /// <summary>Signs data with SHA-256 and returns the 64-byte IEEE P1363 signature.</summary>
    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        return _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _key.Dispose();
    }
}

/// <summary>Verifies publication signatures made by a <see cref="PublicationSigningKey"/>.</summary>
public static class PublicationSigner
{
    private const int KeyIdHashBytes = 16;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="signature"/> is a valid ECDSA P-256 SHA-256 IEEE P1363 signature of
    /// <paramref name="data"/> under <paramref name="publicKey"/>. A malformed key or signature returns <see langword="false"/>.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        using ECDsa key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
            return bytesRead == publicKey.Length
                && IsP256(key)
                && key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Returns the key fingerprint: lowercase hex of the first 16 bytes of SHA-256 over the SubjectPublicKeyInfo.</summary>
    public static string KeyId(ReadOnlySpan<byte> publicKey)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(publicKey, hash);
        return Convert.ToHexStringLower(hash[..KeyIdHashBytes]);
    }

    internal static bool IsP256(ECDsa key)
    {
        return key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
    }
}
