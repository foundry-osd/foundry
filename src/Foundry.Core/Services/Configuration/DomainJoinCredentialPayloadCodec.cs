// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Encodes bounded context-bound credentials for media and operation-owned target payloads.</summary>
public static class DomainJoinCredentialPayloadCodec
{
    private static readonly UTF8Encoding Encoding = new(false, true);
    private static ReadOnlySpan<byte> Magic => "FNDRYDJ1"u8;

    /// <summary>Returns an owned version-1 binary payload. The caller must clear the returned plaintext bytes.</summary>
    public static byte[] Encode(DomainJoinCredentialContext context, ReadOnlySpan<char> password)
    {
        ValidateContext(context);
        ValidatePassword(password);
        string domain = DomainJoinCredentialContext.CanonicalizeDomainName(context.DomainName);
        string account = context.AccountName.Trim();
        int domainLength = Encoding.GetByteCount(domain);
        int accountLength = Encoding.GetByteCount(account);
        int passwordLength = Encoding.GetByteCount(password);
        byte[] bytes = new byte[20 + domainLength + accountLength + passwordLength];
        try
        {
            Magic.CopyTo(bytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), domainLength);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), accountLength);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), passwordLength);
            Encoding.GetBytes(domain, bytes.AsSpan(20, domainLength));
            Encoding.GetBytes(account, bytes.AsSpan(20 + domainLength, accountLength));
            Encoding.GetBytes(password, bytes.AsSpan(20 + domainLength + accountLength));
            return bytes;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    /// <summary>Validates structure and authenticated inner context before returning any password. Media readers must supply the outer expected context.</summary>
    public static DomainJoinCredentialPayload Decode(ReadOnlySpan<byte> bytes, DomainJoinCredentialContext? expected = null)
    {
        if (bytes.Length < 20 || bytes.Length > DomainJoinConfigurationValidator.MaximumCredentialPayloadBytes || !bytes[..8].SequenceEqual(Magic))
            throw new InvalidDataException("The domain credential payload is invalid.");
        int domainLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        int accountLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
        int passwordLength = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        if (domainLength is <= 0 or > 1012 || accountLength is <= 0 or > 2048 ||
            passwordLength is <= 0 or > DomainJoinConfigurationValidator.MaximumPasswordUtf8Bytes ||
            20L + domainLength + accountLength + passwordLength != bytes.Length)
            throw new InvalidDataException("The domain credential payload is invalid.");
        char[]? password = null;
        try
        {
            var context = new DomainJoinCredentialContext(Encoding.GetString(bytes.Slice(20, domainLength)),
                Encoding.GetString(bytes.Slice(20 + domainLength, accountLength)));
            ValidateContext(context);
            if (expected is not null && !context.Matches(expected))
                throw new CryptographicException("The domain credential context does not match.");
            password = new char[Encoding.GetCharCount(bytes.Slice(20 + domainLength + accountLength, passwordLength))];
            Encoding.GetChars(bytes.Slice(20 + domainLength + accountLength, passwordLength), password);
            ValidatePassword(password);
            return new(context, password);
        }
        catch (DecoderFallbackException)
        {
            Clear(password);
            throw new InvalidDataException("The domain credential payload is invalid.");
        }
        catch
        {
            Clear(password);
            throw;
        }
    }

    internal static void ValidateContext(DomainJoinCredentialContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!DomainJoinCredentialContext.IsValidDomainName(context.DomainName) ||
            !DomainJoinConfigurationValidator.IsQualifiedAccount(context.AccountName))
            throw new InvalidDataException("The domain credential context is invalid.");
    }

    public static void ValidatePassword(ReadOnlySpan<char> password)
    {
        try
        {
            if (password.IsEmpty || password.Contains('\0') || Encoding.GetByteCount(password) > DomainJoinConfigurationValidator.MaximumPasswordUtf8Bytes)
                throw new InvalidDataException("The domain credential password is invalid.");
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidDataException("The domain credential password is invalid.");
        }
    }

    private static void Clear(char[]? password)
    {
        if (password is not null) CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
    }
}

/// <summary>Owns decoded plaintext; disposal clears its password memory. The context contains no password.</summary>
public sealed class DomainJoinCredentialPayload : IDisposable
{
    private readonly char[] password;
    private bool isDisposed;
    internal DomainJoinCredentialPayload(DomainJoinCredentialContext context, char[] password)
    {
        Context = context;
        this.password = password;
    }
    public DomainJoinCredentialContext Context { get; }
    /// <summary>Exposes owned memory valid only until disposal; consumers must not retain plaintext copies.</summary>
    public ReadOnlyMemory<char> Password
    {
        get
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return password;
        }
    }
    public void Dispose()
    {
        if (isDisposed) return;
        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
        isDisposed = true;
    }
}
