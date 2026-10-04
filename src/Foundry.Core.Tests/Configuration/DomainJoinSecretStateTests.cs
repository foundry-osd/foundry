// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class DomainJoinSecretStateTests
{
    private static readonly DomainJoinCredentialContext Context = new("example.com", "EXAMPLE\\joiner");

    [Fact]
    public void CodecPreservesPasswordAndClearsDecodedBuffer()
    {
        byte[] encoded = DomainJoinCredentialPayloadCodec.Encode(Context, " exact password ");
        using DomainJoinCredentialPayload decoded = DomainJoinCredentialPayloadCodec.Decode(encoded, new("EXAMPLE.COM.", "example\\JOINER"));
        ReadOnlyMemory<char> owned = decoded.Password;
        Assert.Equal(" exact password ", new string(owned.Span));
        decoded.Dispose();
        Assert.All(owned.ToArray(), value => Assert.Equal('\0', value));
    }

    [Fact]
    public void CodecRejectsContextSubstitutionAndMalformedPayload()
    {
        byte[] encoded = DomainJoinCredentialPayloadCodec.Encode(Context, "password");
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded, new("other.com", Context.AccountName)));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded, new("example.com", "EXAMPLE\\other")));
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded.AsSpan(0, encoded.Length - 1)));
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode(new byte[32769]));
    }

    [Theory]
    [InlineData("\0")]
    public void CodecRejectsUnsafePassword(string password)
    {
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Encode(Context, password));
    }

    [Fact]
    public void CodecRejectsUnpairedSurrogate()
    {
        string password = new('\ud800', 1);
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Encode(Context, password));
    }

    [Fact]
    public void CodecRejectsTrailingBytesAndInvalidUtf8()
    {
        byte[] encoded = DomainJoinCredentialPayloadCodec.Encode(Context, "password");
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode([.. encoded, 0]));
        encoded[^1] = 0xff;
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Decode(encoded));
    }

    [Fact]
    public void CodecEnforcesPasswordUtf8Bound()
    {
        Assert.Throws<InvalidDataException>(() => DomainJoinCredentialPayloadCodec.Encode(Context, new string('é', 1281)));
        using var state = new DomainJoinSecretState();
        Assert.Throws<InvalidDataException>(() => state.SetPassword(Context, new string('a', 2561)));
    }

    [Fact]
    public void ContextChangeClearsPassword()
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword(Context, " original password ");
        Assert.True(state.HasPassword(new(" EXAMPLE.COM. ", "example\\JOINER")));
        Assert.Equal(" original password ", new string(state.GetPasswordCopy(Context)!));
        Assert.True(state.Update(new() { IsEnabled = true, Mode = DomainJoinMode.Automatic, DomainName = "other.example.com", AccountName = Context.AccountName }));
        Assert.False(state.HasPassword(Context));
        Assert.Null(state.GetPasswordCopy(Context));
    }

    [Theory]
    [InlineData(false, DomainJoinMode.Automatic)]
    [InlineData(true, DomainJoinMode.Interactive)]
    public void LeavingAutomaticModeClearsPassword(bool enabled, DomainJoinMode mode)
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword(Context, "password");
        Assert.True(state.Update(new() { IsEnabled = enabled, Mode = mode, DomainName = Context.DomainName, AccountName = Context.AccountName }));
        Assert.False(state.HasPassword(Context));
    }

    [Fact]
    public void OwnedCopiesAreZeroedOnDispose()
    {
        var state = new DomainJoinSecretState();
        char[] input = "password".ToCharArray();
        state.SetPassword(Context, input);
        char[] owned = (char[])typeof(DomainJoinSecretState).GetField("password", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        state.Dispose();
        Assert.All(owned, value => Assert.Equal('\0', value));
        Assert.Equal("password", new string(input));
        Assert.Throws<ObjectDisposedException>(() => state.GetPasswordCopy(Context));
    }

    [Fact]
    public void DifferentContextCannotReadPassword()
    {
        using var state = new DomainJoinSecretState();
        state.SetPassword(Context, "password");
        Assert.False(state.HasPassword(new("example.com", "EXAMPLE\\other")));
        Assert.Null(state.GetPasswordCopy(new("other.com", Context.AccountName)));
    }
}
