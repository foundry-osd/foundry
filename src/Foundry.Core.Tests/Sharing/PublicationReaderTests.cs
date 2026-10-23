// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class PublicationReaderTests : IDisposable
{
    private const string Password = "publication-password";

    private readonly TemporaryDirectory directory = new();
    private readonly PublicationSigningKey signer = PublicationSigningKey.Create();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Folder => directory.Path;

    [Fact]
    public async Task Read_UpToDate_DoesNotOpenManifest()
    {
        await PublishAsync();
        using FileStream locked = new(await CurrentManifestPathAsync(), FileMode.Open, FileAccess.Read, FileShare.None);

        PublicationReadResult result = await PublicationReader.ReadAsync(Folder, Password, signer.PublicKey, 1, Cancellation);

        Assert.Equal(new PublicationReadResult(PublicationReadStatus.UpToDate, null, 1, null), result);
    }

    [Fact]
    public async Task Read_HigherSequence_ReturnsPayload()
    {
        await PublishAsync();
        await PublishAsync();

        PublicationReadResult result = await PublicationReader.ReadAsync(Folder, Password, signer.PublicKey, 1, Cancellation);

        Assert.Equal(PublicationReadStatus.Ok, result.Status);
        Assert.Equal(2, result.Sequence);
        Assert.Equal(signer.KeyId, result.SignerKeyId);
        Assert.Equal(2, result.Payload!.Sequence);
        Assert.Equal(PackageKind.Publication, result.Payload.Kind);
        Assert.NotNull(result.Payload.MasterKey);
    }

    [Fact]
    public async Task Read_LowerSequence_ReturnsRollbackRejected()
    {
        await PublishAsync();

        PublicationReadResult result = await PublicationReader.ReadAsync(Folder, Password, signer.PublicKey, 5, Cancellation);

        Assert.Equal(new PublicationReadResult(PublicationReadStatus.RollbackRejected, null, 1, null), result);
    }

    [Fact]
    public async Task Read_PointerAheadOfManifest_ReturnsNotYetAvailable()
    {
        await PublishAsync();
        await File.WriteAllTextAsync(
            Path.Combine(Folder, "current.json"),
            $$"""{"format":"foundry-publication","version":1,"manifest":"manifests/000002-{{Guid.NewGuid():N}}.fcfg"}""",
            Cancellation);

        PublicationReadResult result = await PublicationReader.ReadAsync(Folder, Password, signer.PublicKey, 1, Cancellation);

        Assert.Equal(new PublicationReadResult(PublicationReadStatus.NotYetAvailable, null, 2, null), result);
    }

    [Fact]
    public async Task Read_WrongPassword_ReturnsWrongPassword()
    {
        await PublishAsync();

        PublicationReadResult result = await PublicationReader.ReadAsync(Folder, "another-publication-password", signer.PublicKey, 0, Cancellation);

        Assert.Equal(PublicationReadStatus.WrongPassword, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task Read_ForeignSigner_ReturnsUnknownPublisherWithKeyId()
    {
        await PublishAsync();
        using PublicationSigningKey pinned = PublicationSigningKey.Create();

        PublicationReadResult result = await PublicationReader.ReadAsync(Folder, Password, pinned.PublicKey, 0, Cancellation);

        Assert.Equal(new PublicationReadResult(PublicationReadStatus.UnknownPublisher, null, 1, signer.KeyId), result);
    }

    [Fact]
    public async Task Read_ModifiedManifest_ReturnsTampered()
    {
        await PublishAsync();
        string manifest = await CurrentManifestPathAsync();
        JsonObject envelope = JsonNode.Parse(await File.ReadAllBytesAsync(manifest, Cancellation))!.AsObject();
        byte[] ciphertext = Convert.FromBase64String(envelope["ciphertext"]!.GetValue<string>());
        ciphertext[0] ^= 0x01;
        envelope["ciphertext"] = Convert.ToBase64String(ciphertext);
        await File.WriteAllTextAsync(manifest, envelope.ToJsonString(), Cancellation);

        PublicationReadResult result = await PublicationReader.ReadAsync(Folder, Password, signer.PublicKey, 0, Cancellation);

        Assert.Equal(PublicationReadStatus.Tampered, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task Preview_ReturnsPublisherKey()
    {
        await PublishAsync();

        PublicationReadResult result = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);

        Assert.Equal(PublicationReadStatus.Ok, result.Status);
        Assert.Equal(1, result.Sequence);
        Assert.Equal(signer.KeyId, result.SignerKeyId);
        Assert.Equal(signer.PublicKey, result.Payload!.Publisher!.PublicKey);
        Assert.Equal("REFERENCE-PC", result.Payload.Publisher.Name);
    }

    [Fact]
    public async Task Preview_ExportInPlaceOfManifest_ReturnsTampered()
    {
        await PublishAsync();
        ConfigurationPackagePayload export = new() { Kind = PackageKind.Export, Sequence = 1, Configuration = new FoundryConfigurationDocument() };
        await File.WriteAllBytesAsync(await CurrentManifestPathAsync(), ConfigurationPackageCodec.Write(export, Password), Cancellation);

        PublicationReadResult result = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);

        Assert.Equal(PublicationReadStatus.Tampered, result.Status);
        Assert.Null(result.Payload);
    }

    public void Dispose()
    {
        signer.Dispose();
        directory.Dispose();
    }

    private async Task PublishAsync()
    {
        PublicationRequest request = new(
            Folder,
            new FoundryConfigurationDocument(),
            [],
            null,
            signer,
            RandomNumberGenerator.GetBytes(32),
            Password,
            Guid.NewGuid(),
            "REFERENCE-PC");
        PublishResult result = await PublicationWriter.PublishAsync(request, new NoContent(), null, Cancellation);
        Assert.Equal(PublishStatus.Published, result.Status);
    }

    private async Task<string> CurrentManifestPathAsync()
    {
        using JsonDocument pointer = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Folder, "current.json"), Cancellation));
        return Path.Combine(Folder, pointer.RootElement.GetProperty("manifest").GetString()!);
    }

    private sealed class NoContent : IContentSource
    {
        public Task<Stream> OpenAsync(PackageContent content, CancellationToken ct) => throw new InvalidOperationException("No content is published.");
    }
}
