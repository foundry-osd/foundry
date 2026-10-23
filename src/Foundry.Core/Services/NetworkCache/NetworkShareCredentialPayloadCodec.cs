// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Identifies the share and account a network cache password belongs to.</summary>
public sealed record NetworkShareCredentialContext(string SharePath, string AccountName)
{
    /// <summary>Compares ownership case-insensitively and ignores a trailing separator on the share path.</summary>
    public bool Matches(NetworkShareCredentialContext other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(CanonicalizeSharePath(SharePath), CanonicalizeSharePath(other.SharePath), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(AccountName.Trim(), other.AccountName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    internal static string CanonicalizeSharePath(string value) => value.Trim().TrimEnd('\\');
}

/// <summary>
/// Encodes a share password bound to its share path and account, so a payload copied to another share entry
/// never decodes. Same binary shape as the domain join payload with its own magic.
/// </summary>
public static class NetworkShareCredentialPayloadCodec
{
    public const int MaximumPasswordUtf8Bytes = 2560;
    private const int MaximumPathUtf8Bytes = 4096;
    private const int MaximumAccountUtf8Bytes = 2048;
    private const int HeaderLength = 20;
    private static readonly UTF8Encoding Encoding = new(false, true);
    private static ReadOnlySpan<byte> Magic => "FNDRYNC1"u8;

    /// <summary>Returns an owned payload. The caller must clear the returned plaintext bytes.</summary>
    public static byte[] Encode(NetworkShareCredentialContext context, ReadOnlySpan<char> password)
    {
        ValidateContext(context);
        ValidatePassword(password);
        string path = NetworkShareCredentialContext.CanonicalizeSharePath(context.SharePath);
        string account = context.AccountName.Trim();
        int pathLength = Encoding.GetByteCount(path);
        int accountLength = Encoding.GetByteCount(account);
        int passwordLength = Encoding.GetByteCount(password);
        byte[] bytes = new byte[HeaderLength + pathLength + accountLength + passwordLength];
        try
        {
            Magic.CopyTo(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), pathLength);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), accountLength);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), passwordLength);
            Encoding.GetBytes(path, bytes.AsSpan(HeaderLength, pathLength));
            Encoding.GetBytes(account, bytes.AsSpan(HeaderLength + pathLength, accountLength));
            Encoding.GetBytes(password, bytes.AsSpan(HeaderLength + pathLength + accountLength));
            return bytes;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    /// <summary>Validates structure and the expected share and account before returning any password.</summary>
    public static NetworkShareCredentialPayload Decode(ReadOnlySpan<byte> bytes, NetworkShareCredentialContext expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (bytes.Length < HeaderLength || !bytes[..8].SequenceEqual(Magic))
            throw new InvalidDataException("The network share credential payload is invalid.");
        int pathLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int accountLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        int passwordLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        if (pathLength is <= 0 or > MaximumPathUtf8Bytes || accountLength is <= 0 or > MaximumAccountUtf8Bytes ||
            passwordLength is <= 0 or > MaximumPasswordUtf8Bytes ||
            (long)HeaderLength + pathLength + accountLength + passwordLength != bytes.Length)
            throw new InvalidDataException("The network share credential payload is invalid.");
        char[]? password = null;
        try
        {
            var context = new NetworkShareCredentialContext(
                Encoding.GetString(bytes.Slice(HeaderLength, pathLength)),
                Encoding.GetString(bytes.Slice(HeaderLength + pathLength, accountLength)));
            if (!context.Matches(expected))
                throw new CryptographicException("The network share credential context does not match.");
            ReadOnlySpan<byte> passwordBytes = bytes.Slice(HeaderLength + pathLength + accountLength, passwordLength);
            password = new char[Encoding.GetCharCount(passwordBytes)];
            Encoding.GetChars(passwordBytes, password);
            ValidatePassword(password);
            return new(context, password);
        }
        catch (DecoderFallbackException)
        {
            Clear(password);
            throw new InvalidDataException("The network share credential payload is invalid.");
        }
        catch
        {
            Clear(password);
            throw;
        }
    }

    private static void ValidateContext(NetworkShareCredentialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.SharePath) || string.IsNullOrWhiteSpace(context.AccountName))
            throw new InvalidDataException("The network share credential context is invalid.");
    }

    private static void ValidatePassword(ReadOnlySpan<char> password)
    {
        try
        {
            if (password.IsEmpty || password.Contains('\0') || Encoding.GetByteCount(password) > MaximumPasswordUtf8Bytes)
                throw new InvalidDataException("The network share credential password is invalid.");
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidDataException("The network share credential password is invalid.");
        }
    }

    private static void Clear(char[]? password)
    {
        if (password is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
    }
}

/// <summary>Owns a decoded password; disposal clears it. The context contains no password.</summary>
public sealed class NetworkShareCredentialPayload : IDisposable
{
    private readonly char[] password;

    internal NetworkShareCredentialPayload(NetworkShareCredentialContext context, char[] password)
    {
        Context = context;
        this.password = password;
    }

    public NetworkShareCredentialContext Context { get; }

    /// <summary>Exposes owned memory valid only until disposal; consumers must not keep plaintext copies.</summary>
    public ReadOnlyMemory<char> Password => password;

    public void Dispose() => CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
}
