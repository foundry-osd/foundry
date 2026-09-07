// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.Catalog;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Services.Catalog;

namespace Foundry.Deploy.Tests;

public sealed class CatalogSnapshotStoreTests
{
    [Fact]
    public void Publish_InvalidReplacementPreservesPreviousCandidate()
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var document = Document();
            var store = new CatalogSnapshotStore(root, Manifest(document), root);
            store.Publish(document);
            string path = Path.Combine(root, "operating-systems.json");
            byte[] original = File.ReadAllBytes(path);
            Assert.Throws<InvalidDataException>(() => store.Publish(document with { Content = Encoding.UTF8.GetBytes("<changed/>") }));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_UsesPinnedBytesAndRejectsTampering(bool tamper)
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Foundry", "Catalogs"));
        try
        {
            var document = Document();
            string path = Path.Combine(root, VerifiedCatalogSources.GetRelativePath(document.Id));
            await File.WriteAllBytesAsync(path, tamper ? Encoding.UTF8.GetBytes("<changed/>") : document.Content);
            var store = new CatalogSnapshotStore(root, Manifest(document));
            if (tamper) await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(document.Id));
            else Assert.Equal(document.Content, (await store.LoadAsync(document.Id)).Content);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LoadAsync_RejectsWritableEnvelopeWithoutTrustedDescriptor()
    {
        string root = Path.Combine(Path.GetTempPath(), "FoundryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var document = Document();
            var store = new CatalogSnapshotStore(root, Manifest(document) with { CatalogSnapshots = [] }, root);
            await File.WriteAllTextAsync(Path.Combine(root, "operating-systems.json"), System.Text.Json.JsonSerializer.Serialize(document));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(document.Id));
        }
        finally { Directory.Delete(root, true); }
    }

    internal static VerifiedCatalogDocument Document()
    {
        byte[] content = Encoding.UTF8.GetBytes("<Catalog schemaVersion=\"4\"><Sources/><Items/></Catalog>");
        string hash = Convert.ToHexString(SHA256.HashData(content));
        return new(VerifiedCatalogSources.OperatingSystems, content, hash, "sha256:" + hash.ToLowerInvariant(), VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems), DateTimeOffset.UtcNow);
    }
    internal static WinPeMediaManifest Manifest(VerifiedCatalogDocument document) => new(1, Guid.NewGuid(), "win-x64", MediaOperationTarget.Iso, null,
        [new("Foundry.Connect", "win-x64", WinPeProvisioningSource.Release, "v1", new string('A', 64), [new("Foundry.Connect.exe", 1, new string('B', 64))])],
        [new(document.Id, VerifiedCatalogSources.GetRelativePath(document.Id), document.Sha256, document.Revision, document.SourceUri, document.RetrievedUtc, document.Content.LongLength)]);
}
