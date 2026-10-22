// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Tests.Packages;

public sealed class ContentPackageMediaReaderTests : IDisposable
{
    private static readonly ContentPackageKind Kind = ContentPackageKind.PostInstallation;
    private readonly string temp = Path.Combine(Path.GetTempPath(), "Fmr", Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReturnsNotFoundWhenNoRootHasTheManifest()
    {
        string id = Guid.NewGuid().ToString("N");
        var result = await new ContentPackageMediaReader(Kind).ReadAsync([NewRoot(), NewRoot()], id, new string('a', 64), Cancellation);
        Assert.Equal(ContentPackageMediaStatus.NotFound, result.Status);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public async Task ReturnsIncompatibleForAMalformedBinding()
    {
        var reader = new ContentPackageMediaReader(Kind);
        Assert.Equal(ContentPackageMediaStatus.Incompatible, (await reader.ReadAsync([NewRoot()], "not-a-guid", new string('a', 64), Cancellation)).Status);
        Assert.Equal(ContentPackageMediaStatus.Incompatible, (await reader.ReadAsync([NewRoot()], Guid.NewGuid().ToString("N"), "abc", Cancellation)).Status);
    }

    [Fact]
    public async Task ReturnsHashMismatchWhenOnlyATamperedManifestExists()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        WriteMedia(root, CreateMedia(id));
        var result = await new ContentPackageMediaReader(Kind).ReadAsync([root], id, new string('b', 64), Cancellation);
        Assert.Equal(ContentPackageMediaStatus.HashMismatch, result.Status);
    }

    [Fact]
    public async Task SkipsAStaleRootAndUsesTheMatchingOne()
    {
        string id = Guid.NewGuid().ToString("N");
        string stale = NewRoot();
        string current = NewRoot();
        WriteMedia(stale, CreateMedia(id, "stale"));
        string hash = WriteMedia(current, CreateMedia(id));
        var result = await new ContentPackageMediaReader(Kind).ReadAsync([stale, current], id, hash, Cancellation);
        Assert.Equal(ContentPackageMediaStatus.Found, result.Status);
        Assert.Equal(current, result.Root);
    }

    [Fact]
    public async Task ReturnsIncompatibleForNonCanonicalPackagePath()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        var media = CreateMedia(id);
        media = media with { Packages = [media.Packages[0] with { RelativePath = "Cache/PreOobe/Packages/other/files" }] };
        string hash = WriteMedia(root, media);
        var result = await new ContentPackageMediaReader(Kind).ReadAsync([root], id, hash, Cancellation);
        Assert.Equal(ContentPackageMediaStatus.Incompatible, result.Status);
    }

    [Fact]
    public async Task ReturnsFoundWithManifest()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        var media = CreateMedia(id);
        string hash = WriteMedia(root, media);
        var result = await new ContentPackageMediaReader(Kind).ReadAsync([root], id, hash.ToUpperInvariant(), Cancellation);
        Assert.Equal(ContentPackageMediaStatus.Found, result.Status);
        Assert.Equal(root, result.Root);
        Assert.Equal(media.Packages[0].ContentHash, result.Manifest!.Packages.Single().ContentHash);
    }

    [Fact]
    public async Task AcceptsALenientHandWrittenMediaManifest()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        var package = CreateMedia(id).Packages[0];
        var file = package.Manifest.Files[0];
        string json = $$"""
            { // hand-written
              "SchemaVersion": 1, "ID": "{{id}}",
              "packages": [ { "contenthash": "{{package.ContentHash}}", "RelativePath": "{{package.RelativePath}}",
                "manifest": { "files": [ { "relativePath": "{{file.RelativePath}}", "length": {{file.Length}}, "SHA256": "{{file.Sha256}}", } ], } }, ],
            }
            """;
        string directory = Path.Combine(root, "Cache", "PreOobe", "manifests");
        Directory.CreateDirectory(directory);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(Path.Combine(directory, id + ".json"), bytes, Cancellation);
        var result = await new ContentPackageMediaReader(Kind).ReadAsync([root], id, Convert.ToHexString(SHA256.HashData(bytes)), Cancellation);
        Assert.Equal(ContentPackageMediaStatus.Found, result.Status);
    }

    [Fact]
    public async Task ReturnsHashMismatchForAnOversizedManifest()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        string directory = Path.Combine(root, "Cache", "PreOobe", "manifests");
        Directory.CreateDirectory(directory);
        await using (var stream = new FileStream(Path.Combine(directory, id + ".json"), FileMode.Create, FileAccess.Write))
            stream.SetLength(Kind.MaximumMediaManifestBytes + 1L);
        var result = await new ContentPackageMediaReader(Kind).ReadAsync([root], id, new string('a', 64), Cancellation);
        Assert.Equal(ContentPackageMediaStatus.HashMismatch, result.Status);
    }

    [Fact]
    public async Task ReturnsIncompatibleForAnUnsupportedSchemaVersion()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        string hash = WriteMedia(root, CreateMedia(id) with { SchemaVersion = 2 });
        Assert.Equal(ContentPackageMediaStatus.Incompatible, (await new ContentPackageMediaReader(Kind).ReadAsync([root], id, hash, Cancellation)).Status);
    }

    [Fact]
    public async Task ReturnsIncompatibleWhenTheManifestIdDiffersFromTheBinding()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        string hash = WriteMedia(root, CreateMedia(Guid.NewGuid().ToString("N")), id);
        Assert.Equal(ContentPackageMediaStatus.Incompatible, (await new ContentPackageMediaReader(Kind).ReadAsync([root], id, hash, Cancellation)).Status);
    }

    [Fact]
    public async Task ReturnsIncompatibleForMalformedJsonWithAValidHash()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        string hash = WriteRaw(root, id, "{ not json"u8.ToArray());
        Assert.Equal(ContentPackageMediaStatus.Incompatible, (await new ContentPackageMediaReader(Kind).ReadAsync([root], id, hash, Cancellation)).Status);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"id":"{id}","packages":null}""")]
    [InlineData("""{"schemaVersion":1,"id":"{id}","packages":[null]}""")]
    [InlineData("""{"schemaVersion":1,"id":"{id}","packages":[{"contentHash":null,"relativePath":null,"manifest":null}]}""")]
    [InlineData("""{"schemaVersion":1,"id":"{id}","packages":[{"contentHash":"abc","relativePath":"Cache/PreOobe/Packages/abc/files","manifest":{"files":null,"directories":null}}]}""")]
    public async Task ReturnsIncompatibleForNullPackageContent(string template)
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        string hash = WriteRaw(root, id, System.Text.Encoding.UTF8.GetBytes(template.Replace("{id}", id)));
        Assert.Equal(ContentPackageMediaStatus.Incompatible, (await new ContentPackageMediaReader(Kind).ReadAsync([root], id, hash, Cancellation)).Status);
    }

    [Fact]
    public async Task ReturnsTheVerifiedManifestPathWhenFound()
    {
        string id = Guid.NewGuid().ToString("N");
        string root = NewRoot();
        string hash = WriteMedia(root, CreateMedia(id));
        var found = await new ContentPackageMediaReader(Kind).ReadAsync([root], id, hash, Cancellation);
        Assert.Equal(Path.Combine(root, "Cache", "PreOobe", "manifests", id + ".json"), found.ManifestPath);
        Assert.Null((await new ContentPackageMediaReader(Kind).ReadAsync([NewRoot()], id, hash, Cancellation)).ManifestPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
    }

    private string NewRoot()
    {
        string root = Path.Combine(temp, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ContentPackageMediaManifest CreateMedia(string id, string content = "content")
    {
        var manifest = new ContentPackageManifest
        {
            Files = [new ContentPackageFile { RelativePath = "setup.exe", Length = content.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))) }]
        };
        string contentHash = ContentPackageManifestCodec.GetContentHash(manifest);
        return new ContentPackageMediaManifest
        {
            Id = id,
            Packages = [new ContentPackageMediaPackage { ContentHash = contentHash, RelativePath = $"{Kind.MediaRoot}/Packages/{contentHash}/files", Manifest = manifest }]
        };
    }

    private static string WriteMedia(string root, ContentPackageMediaManifest media, string? fileId = null)
        => WriteRaw(root, fileId ?? media.Id, JsonSerializer.SerializeToUtf8Bytes(media, ConfigurationJsonDefaults.SerializerOptions));

    private static string WriteRaw(string root, string id, byte[] bytes)
    {
        string directory = Path.Combine(root, "Cache", "PreOobe", "manifests");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, id + ".json"), bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
