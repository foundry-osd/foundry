// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkShareCredentialPayloadCodecTests
{
    private static readonly NetworkShareCredentialContext Context = new(@"\\fs01\cache", @"CONTOSO\svc-foundry");

    [Fact]
    public void Decode_RoundTripsPasswordForSameShareAndAccount()
    {
        byte[] bytes = NetworkShareCredentialPayloadCodec.Encode(Context, "p@ss word");

        using NetworkShareCredentialPayload payload = NetworkShareCredentialPayloadCodec.Decode(bytes, new(@"\\FS01\cache\", @"contoso\SVC-FOUNDRY"));

        Assert.Equal("p@ss word", new string(payload.Password.Span));
    }

    [Fact]
    public void Decode_ForAnotherShare_Throws()
    {
        byte[] bytes = NetworkShareCredentialPayloadCodec.Encode(Context, "secret");

        Assert.Throws<CryptographicException>(() => NetworkShareCredentialPayloadCodec.Decode(bytes, new(@"\\fs02\cache", Context.AccountName)));
    }

    [Fact]
    public void Decode_TruncatedPayload_Throws()
    {
        byte[] bytes = NetworkShareCredentialPayloadCodec.Encode(Context, "secret");

        Assert.Throws<InvalidDataException>(() => NetworkShareCredentialPayloadCodec.Decode(bytes.AsSpan(0, bytes.Length - 1), Context));
    }

    [Fact]
    public void Encode_EmptyPassword_Throws()
    {
        Assert.Throws<InvalidDataException>(() => NetworkShareCredentialPayloadCodec.Encode(Context, ""));
    }

    [Fact]
    public void Dispose_ClearsPassword()
    {
        NetworkShareCredentialPayload payload = NetworkShareCredentialPayloadCodec.Decode(NetworkShareCredentialPayloadCodec.Encode(Context, "secret"), Context);
        ReadOnlyMemory<char> password = payload.Password;

        payload.Dispose();

        Assert.All(password.ToArray(), character => Assert.Equal('\0', character));
    }
}
