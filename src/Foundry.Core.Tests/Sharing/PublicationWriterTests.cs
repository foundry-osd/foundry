// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class PublicationWriterTests : IDisposable
{
    private const string Password = "publication-password";

    private readonly TemporaryDirectory directory = new();
    private readonly PublicationSigningKey signer = PublicationSigningKey.Create();
    private readonly byte[] masterKey = RandomNumberGenerator.GetBytes(32);
    private readonly FakeContentSource source = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Folder => directory.Path;

    [Fact]
    public async Task Publish_EmptyFolder_WritesSequenceOneAndPointer()
    {
        PackageContent content = source.Add("installer", SharedContentKind.Package);

        PublishResult result = await PublishAsync([content]);

        Assert.Equal(new PublishResult(PublishStatus.Published, 1, null), result);
        using JsonDocument pointer = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Folder, "current.json"), Cancellation));
        Assert.Equal("foundry-publication", pointer.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, pointer.RootElement.GetProperty("version").GetInt32());
        Assert.Matches("^manifests/000001-[0-9a-f]{32}\\.fcfg$", pointer.RootElement.GetProperty("manifest").GetString());
        ConfigurationPackagePayload payload = await ReadCurrentPayloadAsync(Password);
        Assert.Equal(PackageKind.Publication, payload.Kind);
        Assert.Equal(1, payload.Sequence);
        Assert.Equal(masterKey, payload.MasterKey);
        Assert.Equal([content], payload.Contents);
        Assert.Equal("installer", await DecryptContentAsync(content.Hash));
        Assert.Empty(Directory.EnumerateFiles(Folder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Publish_SecondTime_IncrementsSequenceAndReusesContent()
    {
        PackageContent content = source.Add("installer", SharedContentKind.Package);
        await PublishAsync([content]);
        string contentPath = ContentPath(content.Hash);
        DateTime written = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(contentPath, written);

        PublishResult result = await PublishAsync([content]);

        Assert.Equal(new PublishResult(PublishStatus.Published, 2, null), result);
        Assert.Equal(2, (await ReadCurrentPayloadAsync(Password)).Sequence);
        Assert.Equal(written, File.GetLastWriteTimeUtc(contentPath));
        Assert.Equal(1, source.Opened(content.Hash));
    }

    [Fact]
    public async Task Publish_FolderSignedByOtherKey_ReturnsForeignPublisher()
    {
        await PublishAsync([]);
        byte[] pointer = await File.ReadAllBytesAsync(Path.Combine(Folder, "current.json"), Cancellation);
        using PublicationSigningKey other = PublicationSigningKey.Create();

        PublishResult result = await PublicationWriter.PublishAsync(Request([]) with { Signer = other }, source, null, Cancellation);

        Assert.Equal(new PublishResult(PublishStatus.ForeignPublisher, 1, signer.KeyId), result);
        Assert.Equal(pointer, await File.ReadAllBytesAsync(Path.Combine(Folder, "current.json"), Cancellation));
    }

    [Fact]
    public async Task Publish_UnrelatedFiles_ReturnsFolderNotEmpty()
    {
        await File.WriteAllTextAsync(Path.Combine(Folder, "notes.txt"), "unrelated", Cancellation);

        PublishResult result = await PublishAsync([source.Add("installer", SharedContentKind.Package)]);

        Assert.Equal(PublishStatus.FolderNotEmpty, result.Status);
        Assert.Equal([Path.Combine(Folder, "notes.txt")], Directory.EnumerateFileSystemEntries(Folder));
    }

    [Fact]
    public async Task Publish_KeepsTwoManifestsAndDeletesUnreferencedContent()
    {
        PackageContent first = source.Add("first", SharedContentKind.Package);
        PackageContent second = source.Add("second", SharedContentKind.Package);
        PackageContent third = source.Add("third", SharedContentKind.Image);
        await PublishAsync([first]);
        await PublishAsync([second]);

        PublishResult result = await PublishAsync([third]);

        Assert.Equal(3, result.Sequence);
        Assert.Equal(["000002", "000003"], Directory.EnumerateFiles(Path.Combine(Folder, "manifests")).Select(path => Path.GetFileName(path)[..6]).Order());
        Assert.Equal(new[] { second.Hash, third.Hash }.Order(), Directory.EnumerateFiles(Path.Combine(Folder, "content")).Select(Path.GetFileNameWithoutExtension).Order());
    }

    [Fact]
    public async Task Publish_CancelledDuringContent_LeavesPreviousPointer()
    {
        PackageContent kept = source.Add("kept", SharedContentKind.Package);
        await PublishAsync([kept]);
        byte[] pointer = await File.ReadAllBytesAsync(Path.Combine(Folder, "current.json"), Cancellation);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        PackageContent cancelled = source.Add("cancelled", SharedContentKind.Image, onOpen: cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PublicationWriter.PublishAsync(Request([kept, cancelled]), source, null, cancellation.Token));

        Assert.Equal(pointer, await File.ReadAllBytesAsync(Path.Combine(Folder, "current.json"), Cancellation));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Folder, "manifests")));
        Assert.False(File.Exists(ContentPath(cancelled.Hash)));
        Assert.Empty(Directory.EnumerateFiles(Folder, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Rewrap_NewPasswordReadsOldFailsAndContentUntouched()
    {
        PackageContent content = source.Add("installer", SharedContentKind.Package);
        await PublishAsync([content]);
        byte[] contentBytes = await File.ReadAllBytesAsync(ContentPath(content.Hash), Cancellation);
        DateTime contentWritten = File.GetLastWriteTimeUtc(ContentPath(content.Hash));
        const string newPassword = "another-publication-password";

        PublishStatus status = await PublicationWriter.RewrapAsync(Folder, Password, newPassword, signer, Cancellation);

        Assert.Equal(PublishStatus.Published, status);
        ConfigurationPackagePayload payload = await ReadCurrentPayloadAsync(newPassword);
        Assert.Equal(1, payload.Sequence);
        Assert.Equal(masterKey, payload.MasterKey);
        Assert.Equal(PackageReadStatus.WrongPassword, (await ReadCurrentAsync(Password)).Status);
        Assert.Equal(contentBytes, await File.ReadAllBytesAsync(ContentPath(content.Hash), Cancellation));
        Assert.Equal(contentWritten, File.GetLastWriteTimeUtc(ContentPath(content.Hash)));
    }

    [Fact]
    public async Task Publish_DriverSet_ListsDriverSetKindAndWritesOneContentFile()
    {
        PackageContent driverSet = source.Add("drivers", SharedContentKind.DriverSet);

        await PublishAsync([driverSet]);

        Assert.Equal(SharedContentKind.DriverSet, Assert.Single((await ReadCurrentPayloadAsync(Password)).Contents).Kind);
        Assert.Equal([ContentPath(driverSet.Hash)], Directory.EnumerateFiles(Path.Combine(Folder, "content")));
        Assert.Equal("drivers", await DecryptContentAsync(driverSet.Hash));
    }

    [Fact]
    public async Task Publish_SameHashAsPackageAndDriverSet_WritesOneContentFile()
    {
        PackageContent package = source.Add("shared", SharedContentKind.Package);
        PackageContent driverSet = package with { Kind = SharedContentKind.DriverSet };

        await PublishAsync([package, driverSet]);

        Assert.Equal([package, driverSet], (await ReadCurrentPayloadAsync(Password)).Contents);
        Assert.Equal([ContentPath(package.Hash)], Directory.EnumerateFiles(Path.Combine(Folder, "content")));
        Assert.Equal(1, source.Opened(package.Hash));
    }

    [Fact]
    public async Task Publish_PointerMovedBack_UsesSequenceAboveEveryManifestAndKeepsTheHigherOne()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        await PublishAsync([]);
        string higher = ManifestNames().Single(name => name.StartsWith("000003-", StringComparison.Ordinal));
        await WritePointerAsync(ManifestNames().Single(name => name.StartsWith("000002-", StringComparison.Ordinal)));

        PublishResult result = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.Published, 4, null), result);
        Assert.Equal(4, (await ReadCurrentPayloadAsync(Password)).Sequence);
        Assert.Contains(higher, ManifestNames());
        Assert.Equal(["000003", "000004"], ManifestNames().Select(name => name[..6]).Order());
    }

    [Fact]
    public async Task Publish_PointerDeleted_ContinuesSequence()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        File.Delete(Path.Combine(Folder, "current.json"));

        PublishResult result = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.Published, 3, null), result);
        Assert.Equal(3, (await ReadCurrentPayloadAsync(Password)).Sequence);
    }

    [Fact]
    public async Task Publish_LastPublishedSequenceHigherThanFolder_Wins()
    {
        await PublishAsync([]);

        PublishResult result = await PublicationWriter.PublishAsync(Request([]) with { LastPublishedSequence = 10 }, source, null, Cancellation);

        Assert.Equal(new PublishResult(PublishStatus.Published, 11, null), result);
        Assert.Equal(11, (await ReadCurrentPayloadAsync(Password)).Sequence);
    }

    [Fact]
    public async Task Publish_ContentUnderOtherMasterKey_IsRewritten()
    {
        PackageContent content = source.Add("installer", SharedContentKind.Package);
        await PublishAsync([content]);
        byte[] newMasterKey = RandomNumberGenerator.GetBytes(32);

        await PublicationWriter.PublishAsync(Request([content]) with { MasterKey = newMasterKey }, source, null, Cancellation);

        Assert.Equal(2, source.Opened(content.Hash));
        Assert.Equal("installer", await DecryptContentAsync(content.Hash, newMasterKey));
    }

    [Fact]
    public async Task Publish_RemovesTemporaryLeftovers()
    {
        PackageContent content = source.Add("installer", SharedContentKind.Package);
        await PublishAsync([content]);
        string[] leftovers =
        [
            ContentPath(content.Hash) + ".tmp",
            Path.Combine(Folder, "current.json.tmp"),
            Path.Combine(Folder, "manifests", $"000001-{Guid.NewGuid():N}.fcfg.tmp")
        ];
        foreach (string leftover in leftovers)
        {
            await File.WriteAllTextAsync(leftover, "partial", Cancellation);
        }

        await PublishAsync([content]);

        Assert.All(leftovers, leftover => Assert.False(File.Exists(leftover)));
    }

    [Fact]
    public async Task Publish_PlantedHugeNameManifest_IsIgnoredAndLaterPublishesContinue()
    {
        await PublishAsync([]);
        await File.WriteAllTextAsync(Path.Combine(Folder, "manifests", $"999999999999999998-{Guid.NewGuid():N}.fcfg"), "garbage", Cancellation);

        PublishResult second = await PublishAsync([]);
        PublishResult third = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.Published, 2, null), second);
        Assert.Equal(new PublishResult(PublishStatus.Published, 3, null), third);
        Assert.Equal(3, (await ReadCurrentPayloadAsync(Password)).Sequence);
    }

    [Fact]
    public async Task Publish_PlantedRenamedCopyOfOldManifest_IsNotPreviousAndRealPreviousSurvives()
    {
        PackageContent first = source.Add("first", SharedContentKind.Package);
        PackageContent second = source.Add("second", SharedContentKind.Package);
        PackageContent third = source.Add("third", SharedContentKind.Package);
        await PublishAsync([first]);
        string oldManifest = ManifestNames().Single(name => name.StartsWith("000001-", StringComparison.Ordinal));
        await PublishAsync([second]);
        string realPrevious = ManifestNames().Single(name => name.StartsWith("000002-", StringComparison.Ordinal));
        File.Copy(Path.Combine(Folder, "manifests", oldManifest), Path.Combine(Folder, "manifests", $"000005-{Guid.NewGuid():N}.fcfg"));

        PublishResult result = await PublishAsync([third]);

        Assert.Equal(3, result.Sequence);
        Assert.Contains(realPrevious, ManifestNames());
        Assert.True(File.Exists(ContentPath(second.Hash)));
        Assert.False(File.Exists(ContentPath(first.Hash)));
    }

    [Fact]
    public async Task Publish_PointerNamesMissingManifest_ContinuesSequence()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        await WritePointerAsync($"000007-{Guid.NewGuid():N}.fcfg");

        PublishResult result = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.Published, 3, null), result);
    }

    [Fact]
    public async Task Publish_LastPublishedSequenceAtLimit_ReturnsSequenceExhausted()
    {
        PublishResult result = await PublicationWriter.PublishAsync(
            Request([]) with { LastPublishedSequence = 999_999_999_999_999_999 }, source, null, Cancellation);

        Assert.Equal(new PublishResult(PublishStatus.SequenceExhausted, 0, null), result);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Folder));
    }

    [Fact]
    public async Task Rewrap_AfterPointerMovedBack_ReencryptsNewestManifest()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        await PublishAsync([]);
        await WritePointerAsync(ManifestNames().Single(name => name.StartsWith("000002-", StringComparison.Ordinal)));
        const string newPassword = "another-publication-password";

        PublishStatus status = await PublicationWriter.RewrapAsync(Folder, Password, newPassword, signer, Cancellation);

        Assert.Equal(PublishStatus.Published, status);
        Assert.Equal(3, (await ReadCurrentPayloadAsync(newPassword)).Sequence);
    }

    public void Dispose()
    {
        signer.Dispose();
        directory.Dispose();
    }

    private PublicationRequest Request(IReadOnlyList<PackageContent> contents) => new(
        Folder,
        new FoundryConfigurationDocument(),
        contents,
        null,
        signer,
        masterKey,
        Password,
        Guid.NewGuid(),
        "REFERENCE-PC");

    private Task<PublishResult> PublishAsync(IReadOnlyList<PackageContent> contents) =>
        PublicationWriter.PublishAsync(Request(contents), source, null, Cancellation);

    private string ContentPath(string hash) => Path.Combine(Folder, "content", hash + ".bin");

    private async Task<PackageReadResult> ReadCurrentAsync(string password)
    {
        using JsonDocument pointer = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(Folder, "current.json"), Cancellation));
        byte[] manifest = await File.ReadAllBytesAsync(Path.Combine(Folder, pointer.RootElement.GetProperty("manifest").GetString()!), Cancellation);
        return ConfigurationPackageCodec.Read(manifest, password, signer.PublicKey);
    }

    private async Task<ConfigurationPackagePayload> ReadCurrentPayloadAsync(string password)
    {
        PackageReadResult result = await ReadCurrentAsync(password);
        Assert.Equal(PackageReadStatus.Ok, result.Status);
        return result.Payload!;
    }

    private string[] ManifestNames() => Directory.EnumerateFiles(Path.Combine(Folder, "manifests")).Select(path => Path.GetFileName(path)).ToArray();

    private Task WritePointerAsync(string manifestName) => File.WriteAllTextAsync(
        Path.Combine(Folder, "current.json"),
        $$"""{"format":"foundry-publication","version":1,"manifest":"manifests/{{manifestName}}"}""",
        Cancellation);

    private async Task<string> DecryptContentAsync(string hash, byte[]? key = null)
    {
        using MemoryStream plaintext = new();
        await ContentTransfer.DecryptAsync(ContentPath(hash), plaintext, ContentTransfer.DeriveContentKey(key ?? masterKey, hash), null, Cancellation);
        return Encoding.UTF8.GetString(plaintext.ToArray());
    }

    /// <summary>In-memory content keyed by hash; counts opens and can run a callback when one is opened.</summary>
    private sealed class FakeContentSource : IContentSource
    {
        private readonly Dictionary<string, (byte[] Bytes, Action? OnOpen)> contents = [];
        private readonly Dictionary<string, int> opened = [];

        public PackageContent Add(string text, SharedContentKind kind, Action? onOpen = null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            contents[hash] = (bytes, onOpen);
            return new PackageContent(hash, kind, bytes.Length, text);
        }

        public int Opened(string hash) => opened.GetValueOrDefault(hash);

        public Task<Stream> OpenAsync(PackageContent content, CancellationToken ct)
        {
            (byte[] bytes, Action? onOpen) = contents[content.Hash];
            opened[content.Hash] = Opened(content.Hash) + 1;
            onOpen?.Invoke();
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
    }
}
