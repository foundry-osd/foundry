// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
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
        Assert.Equal(new ContentProgress(file.Length + package.Length, file.Length + package.Length, string.Empty), reports[^1]);
        Assert.All(reports, report => Assert.InRange(report.Done, 0, report.Total));
        Assert.All(Directory.EnumerateFiles(SyncTemporaryRoot, "*", SearchOption.AllDirectories), path => Assert.Equal(".cleanup-lock", Path.GetFileName(path)));
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
        string wim = Path.Combine(directory.Path, "source.wim");
        await File.WriteAllTextAsync(wim, "test image data", Cancellation);
        CustomImageReference image = await reference.Images.ImportAsync(new(wim, "Corporate image"), cancellationToken: Cancellation);
        ConfigurationPackagePayload manifest = await PublishAsync(new FoundryConfigurationDocument
        {
            CustomImages = new CustomImagesSettings { IsEnabled = true, Images = [image] }
        });
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
    private sealed class Libraries(string root)
    {
        public string Root { get; } = root;

        public AuthoringFileLibrary Files { get; } = new(Path.Combine(root, "files"));

        public ContentPackageLibraryService Packages { get; } = new(Path.Combine(root, "packages"), ContentPackageKind.PostInstallation);

        public CustomDriverSetLibrary DriverSets { get; } = new(Path.Combine(root, "drivers"));

        public CustomImageLibraryService Images { get; } = new(Path.Combine(root, "images"), new MetadataReader());

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
