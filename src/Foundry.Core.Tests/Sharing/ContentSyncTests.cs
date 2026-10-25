// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class ContentSyncTests : IDisposable
{
    private const string Password = "publication-password";

    private readonly TemporaryDirectory directory = new();
    private readonly PublicationSigningKey signer = PublicationSigningKey.Create();
    private readonly byte[] masterKey = RandomNumberGenerator.GetBytes(32);
    private readonly Libraries reference;
    private readonly Libraries follower;

    public ContentSyncTests()
    {
        reference = new Libraries(Path.Combine(directory.Path, "reference"));
        follower = new Libraries(Path.Combine(directory.Path, "follower"));
        Directory.CreateDirectory(Folder);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(directory.Path, "publication");

    private string SyncTemporaryRoot => Path.Combine(directory.Path, "sync-temp");

    [Fact]
    public async Task Fetch_MissingFileAndPackage_ImportsBoth()
    {
        AuthoringFileReference file = reference.Files.Import("<profile />"u8, "profile.xml");
        ContentPackageReference package = await ImportPackageAsync(reference.Packages, ("setup.exe", "installer"), ("support/readme.txt", "readme"));
        ConfigurationPackagePayload manifest = await PublishAsync(WithFileAndPackage(file, package));
        ContentSync sync = follower.Sync(SyncTemporaryRoot);
        Assert.Equal(2, sync.Missing(manifest).Count);
        List<ContentProgress> reports = [];

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, new SynchronousProgress(reports.Add), Cancellation);

        Assert.Equal(2, result.Fetched);
        Assert.Empty(result.Failed);
        Assert.Empty(result.NotYetAvailable);
        Assert.True(follower.Files.IsAvailable(file));
        Assert.Equal("<profile />"u8.ToArray(), follower.Files.ReadVerified(file));
        Assert.True(follower.Packages.IsAvailable(package));
        Assert.Empty(sync.Missing(manifest));
        // A package weighs its download plus its install (archive extraction, then the library import).
        long total = file.Length + (3 * package.Length);
        Assert.Equal(new ContentProgress(total, total, string.Empty), reports[^1]);
        Assert.All(reports, report => Assert.InRange(report.Done, 0, report.Total));
        Assert.All(Directory.EnumerateFiles(SyncTemporaryRoot, "*", SearchOption.AllDirectories), path => Assert.Equal(".cleanup-lock", Path.GetFileName(path)));
    }

    [Fact]
    public async Task Fetch_ImageAndDriverSet_ReportsDownloadThenInstallInOrderAndCompletesOnlyAtTheEnd()
    {
        CustomImageReference image = await ImportImageAsync();
        ContentPackageReference set = await ImportPackageAsync(reference.DriverSets.Library, ("net/e1000.inf", "[Version]"), ("net/e1000.sys", "driver"));
        ConfigurationPackagePayload manifest = await PublishAsync(WithImage(image) with { CustomDrivers = WithDriverSet(set).CustomDrivers });
        ContentSync sync = follower.Sync(SyncTemporaryRoot);
        List<ContentProgress> reports = [];

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, new SynchronousProgress(reports.Add), Cancellation);

        Assert.Equal(2, result.Fetched);
        long total = (2 * image.Length) + (3 * set.Length);
        Assert.All(reports, report => Assert.Equal(total, report.Total));
        Assert.Equal(reports.Select(report => report.Done).Order(), reports.Select(report => report.Done));
        double[] percents = reports.Select(SyncProgress.Percent).ToArray();
        Assert.Equal(percents.Order(), percents);
        Assert.All(percents[..^1], percent => Assert.True(percent < 100));
        Assert.Equal(100, percents[^1]);
        Assert.Equal(new ContentProgress(total, total, string.Empty), reports[^1]);
        foreach (string name in new[] { image.DisplayName, manifest.Contents.Single(content => content.Kind == SharedContentKind.DriverSet).Name })
        {
            Assert.Equal(
                [ContentProgressStep.Transferring, ContentProgressStep.Installing],
                reports.Where(report => report.CurrentName == name).Select(report => report.Step).Distinct());
        }
    }

    [Fact]
    public async Task Fetch_AlreadyPresent_SkipsAndReportsZero()
    {
        AuthoringFileReference file = reference.Files.Import("<profile />"u8, "profile.xml");
        ContentPackageReference package = await ImportPackageAsync(reference.Packages, ("setup.exe", "installer"));
        ConfigurationPackagePayload manifest = await PublishAsync(WithFileAndPackage(file, package));
        ContentSync sync = follower.Sync(SyncTemporaryRoot);
        await sync.FetchAsync(Folder, manifest, null, Cancellation);
        Directory.Delete(Path.Combine(Folder, "content"), recursive: true);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(0, result.Fetched);
        Assert.Empty(result.Failed);
        Assert.Empty(result.NotYetAvailable);
    }

    [Fact]
    public async Task Fetch_ContentNotYetArrived_ReportsNotYetAvailable()
    {
        AuthoringFileReference file = reference.Files.Import("<profile />"u8, "profile.xml");
        ContentPackageReference package = await ImportPackageAsync(reference.Packages, ("setup.exe", "installer"));
        FoundryConfigurationDocument configuration = WithFileAndPackage(file, package);
        ConfigurationPackagePayload manifest = await PublishAsync(configuration);
        File.Delete(ContentPath(file.ContentHash));
        // A content file rewritten in place by "Reset publication" fails authentication while it is incomplete.
        byte[] encrypted = await File.ReadAllBytesAsync(ContentPath(package.ContentHash), Cancellation);
        encrypted[^1] ^= 0xFF;
        await File.WriteAllBytesAsync(ContentPath(package.ContentHash), encrypted, Cancellation);
        ContentSync sync = follower.Sync(SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(0, result.Fetched);
        Assert.Empty(result.Failed);
        Assert.Equal(manifest.Contents.OrderBy(content => content.Hash), result.NotYetAvailable.OrderBy(content => content.Hash));
        Assert.False(follower.Files.IsAvailable(file));
        Assert.False(follower.Packages.IsAvailable(package));

        await PublishAsync(configuration);
        ContentSyncResult retried = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(2, retried.Fetched);
        Assert.Empty(retried.NotYetAvailable);
        Assert.Empty(sync.Missing(manifest));
    }

    [Fact]
    public async Task Fetch_CorruptContent_ReportsFailedAndImportsNothing()
    {
        AuthoringFileReference file = reference.Files.Import("<profile />"u8, "profile.xml");
        ContentPackageReference package = await ImportPackageAsync(reference.Packages, ("setup.exe", "installer"));
        // The reference published plaintext that does not match the listed hashes: content that decrypts but is wrong.
        ReplacingContentSource corrupt = new(content => content.Kind == SharedContentKind.File ? "<other />"u8.ToArray() : "not an archive"u8.ToArray());
        ConfigurationPackagePayload manifest = await PublishAsync(WithFileAndPackage(file, package), corrupt);
        ContentSync sync = follower.Sync(SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(0, result.Fetched);
        Assert.Equal(manifest.Contents.OrderBy(content => content.Hash), result.Failed.OrderBy(content => content.Hash));
        Assert.Empty(result.NotYetAvailable);
        Assert.False(Directory.Exists(Path.Combine(follower.Root, "files", "content")) &&
            Directory.EnumerateFileSystemEntries(Path.Combine(follower.Root, "files", "content")).Any());
        Assert.False(Directory.Exists(Path.Combine(follower.Root, "packages", "content")) &&
            Directory.EnumerateFileSystemEntries(Path.Combine(follower.Root, "packages", "content")).Any());
        Assert.Equal(2, sync.Missing(manifest).Count);
    }

    [Fact]
    public async Task Fetch_DriverSet_ImportsIntoDriversLibraryWithLongHostPath()
    {
        string relativePath = "drivers/" + new string('a', 138) + ".inf";
        ContentPackageReference set = await ImportPackageAsync(reference.DriverSets.Library, (relativePath, "[Version]"));
        ConfigurationPackagePayload manifest = await PublishAsync(WithDriverSet(set));
        string driversRoot = Path.Combine(directory.Path, new string('n', 100), new string('m', 100), "library");
        CustomDriverSetLibrary drivers = new(driversRoot);
        ContentSync sync = new(follower.Files, follower.Packages, drivers, follower.Images, SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(1, result.Fetched);
        Assert.Empty(result.Failed);
        Assert.Empty(result.NotYetAvailable);
        Assert.True(drivers.IsAvailable(set));
        Assert.True(Path.Combine(driversRoot, "content", set.ContentHash, "files", relativePath).Length > 260);
        Assert.False(follower.Packages.IsAvailable(set));
    }

    [Fact]
    public async Task Fetch_DriverSetBreakingKindRules_ReportsFailed()
    {
        // A post-installation package (no INF file) published as a driver set, as a modified reference could.
        ContentPackageReference package = await ImportPackageAsync(reference.Packages, ("setup.exe", "installer"));
        ArchiveContentSource source = new(reference.Packages);
        ConfigurationPackagePayload manifest = await PublishAsync(WithDriverSet(package), source);
        ContentSync sync = follower.Sync(SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(0, result.Fetched);
        Assert.Equal(manifest.Contents, result.Failed);
        Assert.Empty(result.NotYetAvailable);
        Assert.False(follower.DriverSets.IsAvailable(package));
        Assert.False(Directory.Exists(Path.Combine(follower.Root, "drivers", "content")) &&
            Directory.EnumerateFileSystemEntries(Path.Combine(follower.Root, "drivers", "content")).Any());
    }

    [Fact]
    public async Task Fetch_Image_ImportsWimAndMetadataIntoImageLibrary()
    {
        CustomImageReference image = await ImportImageAsync();
        ConfigurationPackagePayload manifest = await PublishAsync(WithImage(image));
        Assert.Equal(new PackageContent(image.ContentHash, SharedContentKind.Image, image.Length, image.DisplayName), Assert.Single(manifest.Contents));
        ContentSync sync = follower.Sync(SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(1, result.Fetched);
        Assert.Empty(result.Failed);
        Assert.Empty(result.NotYetAvailable);
        Assert.True(follower.Images.IsAvailable(image));
        CustomImageReference imported = Assert.Single(await follower.Images.ListAsync(Cancellation));
        Assert.Equal(image.ContentHash, imported.ContentHash);
        Assert.Equal(image.Indexes.Select(index => index.Index), imported.Indexes.Select(index => index.Index));
        await using CustomImageSourceLease lease = await follower.Images.AcquireAsync(image, Cancellation);
        Assert.Equal("test image data", await File.ReadAllTextAsync(lease.ImagePath, Cancellation));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fetch_ImageNotMatchingItsReference_ReportsFailedAndImportsNothing(bool recordMatches)
    {
        CustomImageReference image = await ImportImageAsync();
        // Either the record names the image but the WIM bytes differ (same length), or the WIM is right and the record names another hash.
        byte[] header = LocalContentSource.CreateImageHeader(recordMatches ? image : image with { ContentHash = new string('a', 64) });
        byte[] wim = Encoding.UTF8.GetBytes(recordMatches ? "Test image data" : "test image data");
        ReplacingContentSource source = new(_ => [.. header, .. wim]);
        ConfigurationPackagePayload manifest = await PublishAsync(WithImage(image), source);
        ContentSync sync = follower.Sync(SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(0, result.Fetched);
        Assert.Equal(manifest.Contents, result.Failed);
        Assert.Empty(result.NotYetAvailable);
        Assert.Empty(await follower.Images.ListAsync(Cancellation));
        Assert.False(follower.Images.IsAvailable(image));
        AssertNoLeftovers(Path.Combine(follower.Root, "images", "pending"));
    }

    [Fact]
    public async Task Fetch_CancelledDuringImport_LeavesNoTemporaryOrPendingContent()
    {
        CustomImageReference image = await ImportImageAsync();
        ConfigurationPackagePayload manifest = await PublishAsync(WithImage(image));
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        // Cancels once the WIM is staged in the library's pending directory, while the import inspects it.
        Libraries cancelling = new(Path.Combine(directory.Path, "cancelling"), new CallbackMetadataReader(cancellation.Cancel));
        ContentSync sync = cancelling.Sync(SyncTemporaryRoot);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync.FetchAsync(Folder, manifest, null, cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.All(Directory.EnumerateFiles(SyncTemporaryRoot, "*", SearchOption.AllDirectories), path => Assert.Equal(".cleanup-lock", Path.GetFileName(path)));
        AssertNoLeftovers(Path.Combine(cancelling.Root, "images", "pending"));
        Assert.Empty(await cancelling.Images.ListAsync(Cancellation));
    }

    [Fact]
    public async Task Fetch_HashListedAsPackageAndDriverSet_DecryptsOnceAndImportsBoth()
    {
        ContentPackageReference package = await ImportPackageAsync(reference.Packages, ("net/e1000.inf", "[Version]"));
        ContentPackageReference set = await ImportPackageAsync(reference.DriverSets.Library, ("net/e1000.inf", "[Version]"));
        Assert.Equal(package.ContentHash, set.ContentHash);
        FoundryConfigurationDocument configuration = WithDriverSet(set) with
        {
            PreOobe = new PreOobeSettings { IsEnabled = true, Actions = [new PreOobeActionSettings { Name = "Install", Package = package }] }
        };
        ConfigurationPackagePayload manifest = await PublishAsync(configuration);
        Assert.Equal([SharedContentKind.Package, SharedContentKind.DriverSet], manifest.Contents.Select(content => content.Kind).Order());
        Assert.Single(Directory.EnumerateFiles(Path.Combine(Folder, "content")));
        ContentSync sync = follower.Sync(SyncTemporaryRoot);
        List<ContentProgress> reports = [];

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, new SynchronousProgress(reports.Add), Cancellation);

        Assert.Equal(2, result.Fetched);
        Assert.Empty(result.Failed);
        Assert.Empty(result.NotYetAvailable);
        Assert.True(follower.Packages.IsAvailable(package));
        Assert.True(follower.DriverSets.IsAvailable(set));
        // One content item was downloaded once and installed into both libraries: the total counts one download and two installs.
        Assert.All(reports, report => Assert.Equal(5 * set.Length, report.Total));
        Assert.Single(reports, report => report.Done == 0 && report.CurrentName.Length > 0);
    }

    [Fact]
    public async Task Fetch_UnexpectedImporterException_ReportsFailedAndFetchesTheRest()
    {
        AuthoringFileReference file = reference.Files.Import("<profile />"u8, "profile.xml");
        CustomImageReference image = await ImportImageAsync();
        ConfigurationPackagePayload manifest = await PublishAsync(WithImage(image) with
        {
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = file } }
        });
        Libraries failing = new(Path.Combine(directory.Path, "failing"),
            new CallbackMetadataReader(() => throw new COMException("The image metadata cannot be read.")));
        ContentSync sync = failing.Sync(SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(1, result.Fetched);
        Assert.Equal(SharedContentKind.Image, Assert.Single(result.Failed).Kind);
        Assert.Empty(result.NotYetAvailable);
        Assert.True(failing.Files.IsAvailable(file));
        Assert.Empty(await failing.Images.ListAsync(Cancellation));
        AssertNoLeftovers(Path.Combine(failing.Root, "images", "pending"));
    }

    [Fact]
    public async Task Fetch_ItemStillMissingAfterImport_ReportsFailed()
    {
        // A signed configuration whose file name the library stores under another (sanitized) name: importing never makes it present.
        byte[] bytes = "<profile />"u8.ToArray();
        AuthoringFileReference file = new() { ContentHash = Convert.ToHexStringLower(SHA256.HashData(bytes)), FileName = "bad<name>.xml", Length = bytes.Length };
        ConfigurationPackagePayload manifest = await PublishAsync(new FoundryConfigurationDocument
        {
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = file } }
        }, new ReplacingContentSource(_ => bytes));
        ContentSync sync = follower.Sync(SyncTemporaryRoot);

        ContentSyncResult result = await sync.FetchAsync(Folder, manifest, null, Cancellation);

        Assert.Equal(0, result.Fetched);
        Assert.Equal(manifest.Contents, result.Failed);
        Assert.Empty(result.NotYetAvailable);
    }

    [Fact]
    public async Task LocalContentSource_DriverSet_OpensArchiveFromDriversLibrary()
    {
        ContentPackageReference set = await ImportPackageAsync(reference.DriverSets.Library, ("net/e1000.inf", "[Version]"), ("net/e1000.sys", "driver"));
        string sourceRoot = Path.Combine(directory.Path, "source-temp");
        LocalContentSource source = reference.Source(sourceRoot);
        PackageContent content = PortableConfiguration.ListContents(WithDriverSet(set)).Single();
        Assert.Equal(set.Length, content.Length);

        await using (Stream archive = await source.OpenAsync(content, Cancellation))
        {
            Assert.True(archive.CanSeek);
            Assert.Equal(0, archive.Position);
            using (ZipArchive zip = new(archive, ZipArchiveMode.Read, leaveOpen: true))
            {
                Assert.Equal(["manifest.json", "files/net/e1000.inf", "files/net/e1000.sys"], zip.Entries.Select(entry => entry.FullName));
            }

            archive.Position = 0;
            ContentPackageReference imported = await ContentPackageArchive.ImportAsync(follower.DriverSets.Library, archive, set.ContentHash, Cancellation);
            Assert.Equal(set.ContentHash, imported.ContentHash);
        }

        Assert.All(Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories), path => Assert.Equal(".cleanup-lock", Path.GetFileName(path)));
    }

    public void Dispose()
    {
        signer.Dispose();
        directory.Dispose();
    }

    private static FoundryConfigurationDocument WithFileAndPackage(AuthoringFileReference file, ContentPackageReference package) => new()
    {
        Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = file } },
        PreOobe = new PreOobeSettings { IsEnabled = true, Actions = [new PreOobeActionSettings { Name = "Install", Package = package }] }
    };

    private static FoundryConfigurationDocument WithImage(CustomImageReference image) => new()
    {
        CustomImages = new CustomImagesSettings { IsEnabled = true, Images = [image] }
    };

    /// <summary>Asserts that a library pending directory, when it exists, holds nothing.</summary>
    private static void AssertNoLeftovers(string pending)
    {
        if (Directory.Exists(pending))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(pending));
        }
    }

    private async Task<CustomImageReference> ImportImageAsync()
    {
        string wim = Path.Combine(directory.Path, "source.wim");
        await File.WriteAllTextAsync(wim, "test image data", Cancellation);
        return await reference.Images.ImportAsync(new(wim, "Corporate image"), cancellationToken: Cancellation);
    }

    private static FoundryConfigurationDocument WithDriverSet(ContentPackageReference package) => new()
    {
        CustomDrivers = new CustomDriverSetsSettings
        {
            IsEnabled = true,
            Sets = [new CustomDriverSetReference { Id = "set", DisplayName = "Network drivers", Package = package }]
        }
    };

    private async Task<ContentPackageReference> ImportPackageAsync(ContentPackageLibraryService library, params (string Path, string Text)[] files)
    {
        string source = Path.Combine(directory.Path, "sources", Guid.NewGuid().ToString("N"));
        foreach ((string relativePath, string text) in files)
        {
            string path = Path.Combine(source, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, text, Cancellation);
        }

        return await library.ImportAsync(source, Cancellation);
    }

    /// <summary>Publishes <paramref name="configuration"/> with its listed content and returns the manifest a follower reads.</summary>
    private async Task<ConfigurationPackagePayload> PublishAsync(FoundryConfigurationDocument configuration, IContentSource? source = null)
    {
        PublicationRequest request = new(Folder, configuration, PortableConfiguration.ListContents(configuration), null, signer, masterKey, Password,
            Guid.NewGuid(), "REFERENCE-PC");
        PublishResult published = await PublicationWriter.PublishAsync(request, source ?? reference.Source(Path.Combine(directory.Path, "source-temp")), null,
            Cancellation);
        Assert.Equal(PublishStatus.Published, published.Status);
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(PublicationReadStatus.Ok, read.Status);
        return read.Payload!;
    }

    private string ContentPath(string hash) => Path.Combine(Folder, "content", hash + ".bin");

    /// <summary>The four libraries of one PC.</summary>
    private sealed class Libraries(string root, ICustomImageMetadataReader? imageReader = null)
    {
        public string Root { get; } = root;

        public AuthoringFileLibrary Files { get; } = new(Path.Combine(root, "files"));

        public ContentPackageLibraryService Packages { get; } = new(Path.Combine(root, "packages"), ContentPackageKind.PostInstallation);

        public CustomDriverSetLibrary DriverSets { get; } = new(Path.Combine(root, "drivers"));

        public CustomImageLibraryService Images { get; } = new(Path.Combine(root, "images"), imageReader ?? new MetadataReader());

        public ContentSync Sync(string temporaryRoot) => new(Files, Packages, DriverSets, Images, temporaryRoot);

        public LocalContentSource Source(string temporaryRoot) => new(Files, Packages, DriverSets, Images, temporaryRoot);
    }

    /// <summary>Returns other bytes than the library holds, so the published plaintext does not match the listed hash.</summary>
    private sealed class ReplacingContentSource(Func<PackageContent, byte[]> bytes) : IContentSource
    {
        public Task<Stream> OpenAsync(PackageContent content, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(bytes(content), writable: false));
    }

    /// <summary>Serves every content item as the archive of a package of <paramref name="library"/>, whatever its listed kind.</summary>
    private sealed class ArchiveContentSource(ContentPackageLibraryService library) : IContentSource
    {
        public async Task<Stream> OpenAsync(PackageContent content, CancellationToken ct)
        {
            MemoryStream archive = new();
            await ContentPackageArchive.WriteAsync(library, content.Hash, archive, ct);
            archive.Position = 0;
            return archive;
        }
    }

    /// <summary>Runs <paramref name="onRead"/>, which may throw, then returns the indexes of <see cref="MetadataReader"/>.</summary>
    private sealed class CallbackMetadataReader(Action onRead) : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            onRead();
            return new MetadataReader().ReadAsync(imagePath, cancellationToken);
        }
    }

    private sealed class MetadataReader : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomImageIndex>>([
                new() { Index = 1, Name = "Enterprise", EditionId = "Enterprise", Architecture = "amd64" },
                new() { Index = 3, Name = "Education", EditionId = "Education", Architecture = "amd64" }
            ]);
    }

    /// <summary>Records reports on the reporting thread, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class SynchronousProgress(Action<ContentProgress> report) : IProgress<ContentProgress>
    {
        public void Report(ContentProgress value) => report(value);
    }
}
