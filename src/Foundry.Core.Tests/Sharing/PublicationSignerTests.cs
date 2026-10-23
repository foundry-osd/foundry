// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.Sharing;

public sealed class PublicationSignerTests
{
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("manifest");

    [Fact]
    public void Sign_Verify_RoundTrip()
    {
        using PublicationSigningKey key = PublicationSigningKey.Create();

        Assert.True(PublicationSigner.Verify(key.PublicKey, Data, key.Sign(Data)));
    }

    [Fact]
    public void Verify_OtherKey_ReturnsFalse()
    {
        using PublicationSigningKey key = PublicationSigningKey.Create();
        using PublicationSigningKey other = PublicationSigningKey.Create();

        Assert.False(PublicationSigner.Verify(other.PublicKey, Data, key.Sign(Data)));
    }

    [Fact]
    public void Verify_ModifiedData_ReturnsFalse()
    {
        using PublicationSigningKey key = PublicationSigningKey.Create();
        byte[] signature = key.Sign(Data);

        Assert.False(PublicationSigner.Verify(key.PublicKey, Encoding.UTF8.GetBytes("Manifest"), signature));
    }

    [Fact]
    public void ExportImport_PreservesKeyId()
    {
        using PublicationSigningKey key = PublicationSigningKey.Create();
        using PublicationSigningKey imported = PublicationSigningKey.Import(key.ExportPkcs8());

        Assert.Equal(key.KeyId, imported.KeyId);
        Assert.Equal(key.PublicKey, imported.PublicKey);
        Assert.True(PublicationSigner.Verify(key.PublicKey, Data, imported.Sign(Data)));
        Assert.ThrowsAny<CryptographicException>(() => PublicationSigningKey.Import([.. key.ExportPkcs8(), 0]));
    }

    [Fact]
    public void KeyId_Is32LowercaseHex()
    {
        using PublicationSigningKey key = PublicationSigningKey.Create();

        Assert.Matches("^[0-9a-f]{32}$", key.KeyId);
        Assert.Equal(key.KeyId, PublicationSigner.KeyId(key.PublicKey));
    }
}
