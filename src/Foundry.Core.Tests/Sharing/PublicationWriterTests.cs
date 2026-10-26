// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
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
    public async Task Publish_PlaintextLongerThanListedLength_ReportsMonotonicProgress()
    {
        // A package archive is longer than its listed length (the sum of its file lengths).
        PackageContent first = source.Add(new string('a', (2 * ContentTransfer.ChunkSize) + 1), SharedContentKind.Package) with { Length = 10 };
        PackageContent second = source.Add("second", SharedContentKind.Package);
        List<ContentProgress> reports = [];

        await PublicationWriter.PublishAsync(Request([first, second]), source, new SynchronousProgress(reports.Add), Cancellation);

        Assert.Equal(reports.Select(report => report.Done).Order(), reports.Select(report => report.Done));
        Assert.All(reports, report => Assert.InRange(report.Done, 0, first.Length + second.Length));
        Assert.Equal(new ContentProgress(first.Length + second.Length, first.Length + second.Length, string.Empty), reports[^1]);
    }

    [Fact]
    public async Task Publish_WithPreparation_ReportsEveryStepInOrderAndCompletesOnlyAtTheEnd()
    {
        source.ReportsPreparation = true;
        PackageContent present = source.Add("already published", SharedContentKind.Package);
        await PublishAsync([present]);
        PackageContent image = source.Add("image bytes", SharedContentKind.Image);
        PackageContent package = source.Add("package bytes", SharedContentKind.DriverSet);
        List<ContentProgress> reports = [];

        await PublicationWriter.PublishAsync(Request([present, image, package]), source, new SynchronousProgress(reports.Add), Cancellation);

        long total = present.Length + (2 * image.Length) + (2 * package.Length);
        Assert.Equal(ContentProgressStep.CheckingFolder, reports[0].Step);
        Assert.Equal(reports.Select(report => report.Done).Order(), reports.Select(report => report.Done));
        double[] percents = reports.Select(PublishProgress.Percent).ToArray();
        Assert.Equal(percents.Order(), percents);
        Assert.All(percents, percent => Assert.True(percent < 100));
        Assert.Equal(new ContentProgress(total, total, string.Empty), reports[^1]);
        // Content already complete in the folder counts as done at once and is neither prepared nor sent again.
        Assert.Equal(present.Length, reports[1].Done);
        Assert.DoesNotContain(reports, report => report.CurrentName == present.Name);
        Assert.Equal(1, source.Opened(present.Hash));
        Assert.Equal(
            [ContentProgressStep.Verifying, ContentProgressStep.Transferring],
            reports.Where(report => report.CurrentName == image.Name).Select(report => report.Step).Distinct());
        Assert.Equal(
            [ContentProgressStep.Preparing, ContentProgressStep.Transferring],
            reports.Where(report => report.CurrentName == package.Name).Select(report => report.Step).Distinct());
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

    [Fact]
    public async Task Rewrap_StaleCurrentPassword_ReturnsWrongPasswordAndChangesNothing()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        const string newPassword = "another-publication-password";
        Assert.Equal(PublishStatus.Published, await PublicationWriter.RewrapAsync(Folder, Password, newPassword, signer, Cancellation));
        Dictionary<string, byte[]> before = await SnapshotAsync();

        PublishStatus status = await PublicationWriter.RewrapAsync(Folder, Password, "third-publication-password", signer, Cancellation);

        Assert.Equal(PublishStatus.WrongPassword, status);
        Assert.Equal(before, await SnapshotAsync());
        Assert.Equal(2, (await ReadCurrentPayloadAsync(newPassword)).Sequence);
    }

    [Fact]
    public async Task Rewrap_NoManifestOfThisPc_ReturnsNothingToRewrap()
    {
        Directory.CreateDirectory(Path.Combine(Folder, "manifests"));

        PublishStatus status = await PublicationWriter.RewrapAsync(Folder, Password, "another-publication-password", signer, Cancellation);

        Assert.Equal(PublishStatus.NothingToRewrap, status);
        Assert.Empty(Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Publish_StalePassword_ReturnsWrongPassword()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        Assert.Equal(PublishStatus.Published, await PublicationWriter.RewrapAsync(Folder, Password, "another-publication-password", signer, Cancellation));
        Dictionary<string, byte[]> before = await SnapshotAsync();

        PublishResult result = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.WrongPassword, 0, null), result);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public async Task Publish_OtherKeyWithoutUsablePointer_ReturnsForeignPublisher(string pointer)
    {
        await PublishAsync([source.Add("installer", SharedContentKind.Package)]);
        string pointerPath = Path.Combine(Folder, "current.json");
        switch (pointer)
        {
            case "deleted":
                File.Delete(pointerPath);
                break;
            case "malformed":
                await File.WriteAllTextAsync(pointerPath, "{ not json", Cancellation);
                break;
            default:
                await WritePointerAsync($"000009-{Guid.NewGuid():N}.fcfg");
                break;
        }

        Dictionary<string, byte[]> before = await SnapshotAsync();
        using PublicationSigningKey other = PublicationSigningKey.Create();

        PublishResult result = await PublicationWriter.PublishAsync(Request([]) with { Signer = other }, source, null, Cancellation);

        Assert.Equal(new PublishResult(PublishStatus.ForeignPublisher, 1, signer.KeyId), result);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Publish_TakenOverFolderWithoutPointer_ReturnsForeignPublisherToOldReference()
    {
        await PublishAsync([]);
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await WriteForeignManifestAsync(other, 2);
        File.Delete(Path.Combine(Folder, "current.json"));
        Dictionary<string, byte[]> before = await SnapshotAsync();

        PublishResult result = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.ForeignPublisher, 2, other.KeyId), result);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Publish_ForeignAboveStalePasswordManifest_ReturnsForeignPublisher()
    {
        await PublishAsync([]);
        Assert.Equal(PublishStatus.Published, await PublicationWriter.RewrapAsync(Folder, Password, "another-publication-password", signer, Cancellation));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await WriteForeignManifestAsync(other, 2);
        Dictionary<string, byte[]> before = await SnapshotAsync();

        PublishResult result = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.ForeignPublisher, 2, other.KeyId), result);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Rewrap_ForeignManifestAboveOwn_ReturnsForeignPublisher()
    {
        await PublishAsync([]);
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await WriteForeignManifestAsync(other, 2);
        Dictionary<string, byte[]> before = await SnapshotAsync();

        PublishStatus status = await PublicationWriter.RewrapAsync(Folder, Password, "another-publication-password", signer, Cancellation);

        Assert.Equal(PublishStatus.ForeignPublisher, status);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task Publish_ForeignManifestBelowOwn_DoesNotBlock()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await WriteForeignManifestAsync(other, 1);

        PublishResult result = await PublishAsync([]);

        Assert.Equal(new PublishResult(PublishStatus.Published, 3, null), result);
    }

    [Fact]
    public async Task TakeOver_RightPassword_PublishesAboveForeignAndOldReferenceGetsForeignPublisher()
    {
        PackageContent content = source.Add("installer", SharedContentKind.Package);
        await PublishAsync([content]);
        await PublishAsync([content]);
        using PublicationSigningKey other = PublicationSigningKey.Create();
        byte[] newMasterKey = RandomNumberGenerator.GetBytes(32);

        PublishResult result = await PublicationWriter.PublishAsync(
            Request([content]) with { Signer = other, MasterKey = newMasterKey, TakeOver = true }, source, null, Cancellation);

        Assert.Equal(new PublishResult(PublishStatus.Published, 3, null), result);
        PublicationReadResult read = await PublicationReader.ReadAsync(Folder, Password, other.PublicKey, 2, Cancellation);
        Assert.Equal(PublicationReadStatus.Ok, read.Status);
        Assert.Equal(3, read.Sequence);
        Assert.Equal(other.KeyId, read.Payload!.Publisher!.KeyId);
        Assert.Equal("installer", await DecryptContentAsync(content.Hash, newMasterKey));
        Assert.Equal(new PublishResult(PublishStatus.ForeignPublisher, 3, other.KeyId), await PublishAsync([content]));
    }

    [Fact]
    public async Task TakeOver_WrongPassword_ReturnsWrongPasswordAndChangesNothing()
    {
        await PublishAsync([]);
        Dictionary<string, byte[]> before = await SnapshotAsync();
        using PublicationSigningKey other = PublicationSigningKey.Create();

        PublishResult result = await PublicationWriter.PublishAsync(
            Request([]) with { Signer = other, Password = "another-publication-password", TakeOver = true }, source, null, Cancellation);

        Assert.Equal(new PublishResult(PublishStatus.WrongPassword, 0, null), result);
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task TakeOver_PointerDeleted_ContinuesAboveForeignSequence()
    {
        await PublishAsync([]);
        await PublishAsync([]);
        File.Delete(Path.Combine(Folder, "current.json"));
        using PublicationSigningKey other = PublicationSigningKey.Create();

        PublishResult result = await PublicationWriter.PublishAsync(Request([]) with { Signer = other, TakeOver = true }, source, null, Cancellation);

        Assert.Equal(new PublishResult(PublishStatus.Published, 3, null), result);
        Assert.Equal(3, (await PublicationReader.ReadAsync(Folder, Password, other.PublicKey, 0, Cancellation)).Sequence);
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

    /// <summary>Writes a valid publication manifest signed by <paramref name="key"/> at <paramref name="sequence"/>, as another reference PC would.</summary>
    private async Task WriteForeignManifestAsync(PublicationSigningKey key, long sequence)
    {
        ConfigurationPackagePayload payload = new()
        {
            Kind = PackageKind.Publication,
            Sequence = sequence,
            Publisher = new PackagePublisher(Guid.NewGuid(), "OTHER-REFERENCE", key.KeyId, key.PublicKey),
            Configuration = new FoundryConfigurationDocument(),
            MasterKey = RandomNumberGenerator.GetBytes(32)
        };
        string name = string.Create(CultureInfo.InvariantCulture, $"{sequence:D6}-{Guid.NewGuid():N}.fcfg");
        await File.WriteAllBytesAsync(Path.Combine(Folder, "manifests", name), ConfigurationPackageCodec.Write(payload, Password, key), Cancellation);
    }

    /// <summary>Maps every file of the folder (relative path) to its bytes.</summary>
    private async Task<Dictionary<string, byte[]>> SnapshotAsync()
    {
        Dictionary<string, byte[]> files = [];
        foreach (string path in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
        {
            files[Path.GetRelativePath(Folder, path)] = await File.ReadAllBytesAsync(path, Cancellation);
        }

        return files;
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
    private sealed class SynchronousProgress(Action<ContentProgress> report) : IProgress<ContentProgress>
    {
        public void Report(ContentProgress value) => report(value);
    }

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

        /// <summary>Gets or sets whether non-file content has a preparation step as long as its length, reported in two halves.</summary>
        public bool ReportsPreparation { get; set; }

        public int Opened(string hash) => opened.GetValueOrDefault(hash);

        public long PreparationLength(PackageContent content) =>
            ReportsPreparation && content.Kind != SharedContentKind.File ? content.Length : 0;

        public Task<Stream> OpenAsync(PackageContent content, IProgress<long>? preparation, CancellationToken ct)
        {
            long length = PreparationLength(content);
            preparation?.Report(length / 2);
            preparation?.Report(length);
            return OpenAsync(content, ct);
        }

        public Task<Stream> OpenAsync(PackageContent content, CancellationToken ct)
        {
            (byte[] bytes, Action? onOpen) = contents[content.Hash];
            opened[content.Hash] = Opened(content.Hash) + 1;
            onOpen?.Invoke();
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
    }
}
