// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.Catalog;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeMediaManifestStoreTests
{
    [Fact]
    public async Task CreateAndStage_PreservesExactCatalogBytesAndExcludesAuthoringPaths()
    {
        using var fixture = new Fixture();
        WinPeMediaManifest manifest = fixture.Manifest;
        await WinPeMediaManifestStore.StageAsync(manifest, [fixture.Catalog], fixture.Root, TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Catalog.Content, await File.ReadAllBytesAsync(Path.Combine(fixture.Root,
            VerifiedCatalogSources.GetRelativePath(fixture.Catalog.Id)), TestContext.Current.CancellationToken));
        string manifestPath = Path.Combine(fixture.Root, WinPeMediaManifestStore.RelativePath);
        string json = await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("directoryPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"target\":\"Iso\"", json, StringComparison.Ordinal);
        WinPeMediaManifest read = await WinPeMediaManifestStore.ReadAsync(manifestPath, TestContext.Current.CancellationToken);
        Assert.Equal(manifest.MediaId, read.MediaId);
        Assert.Equal(fixture.Catalog.Content.LongLength, Assert.Single(read.CatalogSnapshots).Length);
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("nested/../escape.dll")]
    [InlineData("C:/escape.dll")]
    [InlineData("/escape.dll")]
    [InlineData("nested//file.dll")]
    [InlineData("file.dll:stream")]
    [InlineData("NUL.dll")]
    [InlineData("file.dll.")]
    public void Validate_RejectsUnsafeRuntimePaths(string path)
    {
        using var fixture = new Fixture();
        WinPeRuntimeApplicationManifest application = fixture.Manifest.Applications[0];
        WinPeMediaManifest changed = fixture.Manifest with
        {
            Applications = [application with
        { Files = [.. application.Files, new(path, 1, new string('a', 64))] }]
        };
        Assert.Throws<InvalidDataException>(() => WinPeMediaManifestStore.Validate(changed));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("digest")]
    [InlineData("length")]
    [InlineData("rid")]
    [InlineData("apphost")]
    [InlineData("catalog")]
    [InlineData("media")]
    [InlineData("version")]
    public void Validate_RejectsIncompleteOrAmbiguousIdentity(string change)
    {
        using var fixture = new Fixture();
        WinPeMediaManifest manifest = fixture.Manifest;
        WinPeRuntimeApplicationManifest application = manifest.Applications[0];
        WinPeRuntimeFile file = application.Files[0];
        manifest = change switch
        {
            "duplicate" => manifest with { Applications = [application with { Files = [file, file with { RelativePath = file.RelativePath.ToUpperInvariant() }] }] },
            "digest" => manifest with { Applications = [application with { Files = [file with { Sha256 = "bad" }] }] },
            "length" => manifest with { Applications = [application with { Files = [file with { Length = -1 }] }] },
            "rid" => manifest with { RuntimeIdentifier = "win-unknown" },
            "apphost" => manifest with { Applications = [application with { Files = [file with { RelativePath = "other.dll" }] }] },
            "catalog" => manifest with { CatalogSnapshots = [] },
            "media" => manifest with { MediaId = Guid.Empty },
            "version" => manifest with { Version = 2 },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<InvalidDataException>(() => WinPeMediaManifestStore.Validate(manifest));
    }

    [Fact]
    public void Validate_RejectsUnexpectedRidAndMediaAssociation()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidDataException>(() => WinPeMediaManifestStore.Validate(fixture.Manifest, "win-unknown"));
        Assert.Throws<InvalidDataException>(() => WinPeMediaManifestStore.Validate(fixture.Manifest, expectedMediaId: Guid.NewGuid()));
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("extra")]
    [InlineData("missing")]
    public async Task ValidateRuntime_RejectsChangedFullFileSet(string change)
    {
        using var fixture = new Fixture();
        string apphost = Path.Combine(fixture.RuntimeRoot, "Foundry.Connect.exe");
        if (change == "tamper") await File.WriteAllTextAsync(apphost, "evil", TestContext.Current.CancellationToken);
        if (change == "extra") await File.WriteAllTextAsync(Path.Combine(fixture.RuntimeRoot, "extra.dll"), "extra", TestContext.Current.CancellationToken);
        if (change == "missing") File.Delete(apphost);
        await Assert.ThrowsAsync<InvalidDataException>(() => WinPeMediaManifestStore.ValidateRuntimeAsync(
            fixture.Manifest, "Foundry.Connect", fixture.RuntimeRoot, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidateRuntime_AcceptsExactPreparedFiles()
    {
        using var fixture = new Fixture();
        await WinPeMediaManifestStore.ValidateRuntimeAsync(fixture.Manifest, "Foundry.Connect", fixture.RuntimeRoot,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Stage_RejectsChangedCatalogBeforeWritingAnything()
    {
        using var fixture = new Fixture();
        fixture.Catalog.Content[0] ^= 1;
        await Assert.ThrowsAsync<InvalidDataException>(() => WinPeMediaManifestStore.StageAsync(fixture.Manifest,
            [fixture.Catalog], fixture.Root, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Foundry")));
    }

    [Fact]
    public async Task Read_RejectsDuplicateJsonProperties()
    {
        using var fixture = new Fixture();
        string json = Encoding.UTF8.GetString(WinPeMediaManifestStore.Serialize(fixture.Manifest));
        string path = Path.Combine(fixture.Root, "duplicate.json");
        await File.WriteAllTextAsync(path, json.Replace("\"version\":1", "\"version\":1,\"Version\":1"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => WinPeMediaManifestStore.ReadAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_RejectsMissingRequiredLength()
    {
        using var fixture = new Fixture();
        string json = Encoding.UTF8.GetString(WinPeMediaManifestStore.Serialize(fixture.Manifest));
        string path = Path.Combine(fixture.Root, "missing-length.json");
        await File.WriteAllTextAsync(path, json.Replace("\"length\":4,", ""), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => WinPeMediaManifestStore.ReadAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidateRuntime_RejectsReparseDirectories()
    {
        using var fixture = new Fixture();
        string target = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(target);
        string link = Path.Combine(fixture.RuntimeRoot, "linked");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (UnauthorizedAccessException) { Assert.Skip("This host does not permit managed symbolic-link creation."); }
        await Assert.ThrowsAsync<InvalidDataException>(() => WinPeMediaManifestStore.ValidateRuntimeAsync(
            fixture.Manifest, "Foundry.Connect", fixture.RuntimeRoot, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("fragment")]
    [InlineData("credentials")]
    public void Validate_RejectsModifiedSourceUriIdentity(string change)
    {
        using var fixture = new Fixture();
        WinPeCatalogSnapshot catalog = fixture.Manifest.CatalogSnapshots[0];
        var source = new UriBuilder(catalog.SourceUri);
        if (change == "fragment") source.Fragment = "untrusted";
        else source.UserName = "untrusted";
        WinPeMediaManifest manifest = fixture.Manifest with
        { CatalogSnapshots = [catalog with { SourceUri = source.Uri }] };
        Assert.Throws<InvalidDataException>(() => WinPeMediaManifestStore.Validate(manifest));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "foundry-manifest-" + Guid.NewGuid().ToString("N"));
        public string RuntimeRoot { get; }
        public VerifiedCatalogDocument Catalog { get; }
        public WinPeMediaManifest Manifest { get; }
        public Fixture()
        {
            RuntimeRoot = Path.Combine(Root, "prepared");
            Directory.CreateDirectory(RuntimeRoot);
            byte[] apphost = Encoding.UTF8.GetBytes("fake");
            File.WriteAllBytes(Path.Combine(RuntimeRoot, "Foundry.Connect.exe"), apphost);
            byte[] xml = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("<catalog schemaVersion=\"4\" />\r\n")).ToArray();
            string digest = Convert.ToHexString(SHA256.HashData(xml)).ToLowerInvariant();
            Catalog = new(VerifiedCatalogSources.OperatingSystems, xml, digest, "sha256:" + digest,
                VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems), DateTimeOffset.UtcNow);
            using var prepared = new WinPePreparedRuntimePayloads(Guid.NewGuid(),
                [new("Foundry.Connect", "win-x64", RuntimeRoot, WinPeProvisioningSource.Release, "v1", new string('a', 64),
                    [new("Foundry.Connect.exe", apphost.Length, Convert.ToHexString(SHA256.HashData(apphost)))])]);
            Manifest = WinPeMediaManifestStore.Create(prepared, MediaOperationTarget.Iso, null, [Catalog]);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
