// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheContentIndexCodecTests
{
    [Fact]
    public void Read_RoundTrip_ReturnsValidWithEqualSections()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        NetworkCacheContentIndex index = CreateIndex(signer, sequence: 7);

        byte[] file = NetworkCacheContentIndexCodec.Write(index, signer);
        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 7, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.Valid, status);
        Assert.NotNull(read);
        Assert.Equal(1, read.SchemaVersion);
        Assert.Equal(signer.KeyId, read.KeyId);
        Assert.Equal(7, read.Sequence);
        Assert.Equal(index.PublishedAtUtc, read.PublishedAtUtc);
        Assert.Equal(index.CustomImages, read.CustomImages);
        Assert.True(index.CustomDrivers.Sets.SequenceEqual(read.CustomDrivers.Sets));
        Assert.Equal(index.CustomDrivers.IsEnabled, read.CustomDrivers.IsEnabled);
        Assert.Equal(index.PreOobe.ManifestHash, read.PreOobe.ManifestHash);
    }

    [Fact]
    public void Read_ModifiedPayload_IsSignatureInvalid()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        byte[] file = NetworkCacheContentIndexCodec.Write(CreateIndex(signer, sequence: 5), signer);
        byte[] tampered = ReplaceInPayload(file, "\"sequence\": 5", "\"sequence\": 6");

        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(tampered, signer.PublicKey, 0, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.SignatureInvalid, status);
        Assert.Null(read);
    }

    [Fact]
    public void Read_SignedByAnotherKey_IsSignatureInvalid()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        using PublicationSigningKey trusted = PublicationSigningKey.Create();
        byte[] file = NetworkCacheContentIndexCodec.Write(CreateIndex(signer, sequence: 1), signer);

        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(file, trusted.PublicKey, 0, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.SignatureInvalid, status);
        Assert.Null(read);
    }

    [Fact]
    public void Read_PayloadKeyIdNotMatchingTrustedKey_IsUnexpectedKey()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        NetworkCacheContentIndex index = CreateIndex(signer, sequence: 1) with { KeyId = "00000000000000000000000000000000" };
        byte[] file = SignRaw(JsonSerializer.SerializeToUtf8Bytes(index, ConfigurationJsonDefaults.SerializerOptions), signer);

        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 0, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.UnexpectedKey, status);
        Assert.Null(read);
    }

    [Fact]
    public void Read_UnsupportedSchemaVersion_IsUnsupportedSchema()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        NetworkCacheContentIndex index = CreateIndex(signer, sequence: 1) with { SchemaVersion = 2 };
        byte[] file = NetworkCacheContentIndexCodec.Write(index, signer);

        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 0, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.UnsupportedSchema, status);
        Assert.Null(read);
    }

    [Fact]
    public void Read_SequenceBelowMinimum_IsTooOld()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        byte[] file = NetworkCacheContentIndexCodec.Write(CreateIndex(signer, sequence: 4), signer);

        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 5, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.TooOld, status);
        Assert.Null(read);
    }

    [Fact]
    public void Read_FileOverMaximum_IsTooLarge()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        byte[] file = new byte[NetworkCacheContentIndexCodec.MaximumBytes + 1];

        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 0, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.TooLarge, status);
        Assert.Null(read);
        Assert.Null(NetworkCacheContentIndexCodec.ReadUnverified(file));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"payload\":\"AAAA\",\"signature\":\"AAAA\"}")]
    [InlineData("{\"payload\":\"bnVsbA==\",\"signature\":\"AAAA\"}")]
    [InlineData("{\"payload\":\"!!\",\"signature\":\"AAAA\"}")]
    public void Read_UnparseableFile_IsMalformed(string text)
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        byte[] file = Encoding.UTF8.GetBytes(text);

        NetworkCacheIndexReadStatus status = NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 0, out NetworkCacheContentIndex? read);

        Assert.Equal(NetworkCacheIndexReadStatus.Malformed, status);
        Assert.Null(read);
        Assert.Null(NetworkCacheContentIndexCodec.ReadUnverified(file));
    }

    [Fact]
    public void Write_KeyIdDifferentFromSigner_Throws()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        NetworkCacheContentIndex index = CreateIndex(signer, sequence: 1) with { KeyId = "other" };

        Assert.Throws<ArgumentException>(() => NetworkCacheContentIndexCodec.Write(index, signer));
    }

    [Fact]
    public void Read_PublishedFarFromNow_IsStillValid()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        foreach (DateTimeOffset published in new[] { DateTimeOffset.UtcNow.AddYears(-5), DateTimeOffset.UtcNow.AddYears(5) })
        {
            NetworkCacheContentIndex index = CreateIndex(signer, sequence: 1) with { PublishedAtUtc = published };
            byte[] file = NetworkCacheContentIndexCodec.Write(index, signer);

            Assert.Equal(NetworkCacheIndexReadStatus.Valid, NetworkCacheContentIndexCodec.Read(file, signer.PublicKey, 0, out _));
        }
    }

    [Fact]
    public void ReadUnverified_ReturnsSectionsWithoutAKey()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        NetworkCacheContentIndex index = CreateIndex(signer, sequence: 3);
        byte[] file = NetworkCacheContentIndexCodec.Write(index, signer);

        NetworkCacheContentIndex? read = NetworkCacheContentIndexCodec.ReadUnverified(file);

        Assert.NotNull(read);
        Assert.Equal(signer.KeyId, read.KeyId);
        Assert.Equal(3, read.Sequence);
        Assert.Equal(index.CustomImages, read.CustomImages);
    }

    private static NetworkCacheContentIndex CreateIndex(PublicationSigningKey signer, long sequence)
    {
        return new NetworkCacheContentIndex
        {
            KeyId = signer.KeyId,
            Sequence = sequence,
            PublishedAtUtc = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
            CustomImages = new DeployCustomImagesSettings
            {
                IsEnabled = true,
                DefaultImageId = "image-1",
                ManifestId = "images-manifest",
                ManifestHash = "abc123"
            },
            CustomDrivers = new DeployCustomDriversSettings
            {
                IsEnabled = true,
                Sets = [new DeployCustomDriverSet { Id = "set-1", DisplayName = "Set", ContentHash = "def456", Length = 10, FileCount = 2 }],
                DefaultSetId = "set-1"
            },
            PreOobe = new DeployPreOobeSettings { ManifestId = "pre-oobe", ManifestHash = "789abc" }
        };
    }

    private static byte[] SignRaw(byte[] payload, PublicationSigningKey signer)
    {
        return JsonSerializer.SerializeToUtf8Bytes(
            new { payload, signature = signer.Sign(payload) },
            ConfigurationJsonDefaults.SerializerOptions);
    }

    private static byte[] ReplaceInPayload(byte[] file, string oldText, string newText)
    {
        using JsonDocument document = JsonDocument.Parse(file);
        byte[] payload = document.RootElement.GetProperty("payload").GetBytesFromBase64();
        string text = Encoding.UTF8.GetString(payload);
        Assert.Contains(oldText, text, StringComparison.Ordinal);
        byte[] modified = Encoding.UTF8.GetBytes(text.Replace(oldText, newText, StringComparison.Ordinal));
        byte[] signature = document.RootElement.GetProperty("signature").GetBytesFromBase64();
        return JsonSerializer.SerializeToUtf8Bytes(new { payload = modified, signature }, ConfigurationJsonDefaults.SerializerOptions);
    }
}
