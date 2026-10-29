// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheShareMaintenanceTests : IDisposable
{
    private const string ImagesRoot = "Cache/OperatingSystems/Custom";
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-ncsm-" + Guid.NewGuid().ToString("N"));
    private readonly WinPeContentPackageMediaService drivers = new(ContentPackageKind.Drivers, _ => long.MaxValue);
    private readonly WinPeContentPackageMediaService postInstallation = new(ContentPackageKind.PostInstallation, _ => long.MaxValue);
    private CustomDriverSetLibrary? driverLibrary;
    private ContentPackageLibraryService? postInstallationLibrary;

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private string ContentRoot => Path.Combine(root, "share", NetworkCacheLayout.RootFolderName);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task MeasureAsync_SumsDownloadsAndImportedContentSeparately()
    {
        WriteFile(Path.Combine(ContentRoot, "Downloads", "OperatingSystemImage", "abcd", "image.esd"), 10);
        WriteFile(Path.Combine(ContentRoot, "Downloads", "OemDriverPack", "efgh", "pack.cab"), 5);
        WriteFile(Path.Combine(ContentRoot, "Cache", "OperatingSystems", "Custom", Hash('a'), "image.wim"), 7);
        WriteFile(Path.Combine(ContentRoot, "Cache", "PreOobe", "Packages", Hash('b'), "files", "setup.exe"), 3);

        NetworkCacheShareUsage usage = await NetworkCacheShareMaintenance.MeasureAsync(ContentRoot, Cancellation);

        Assert.Equal(new NetworkCacheShareUsage(15, 10), usage);
    }

    [Fact]
    public async Task MeasureAsync_ReturnsZeroWhenTheShareHasNoFoundryFolder()
    {
        NetworkCacheShareUsage usage = await NetworkCacheShareMaintenance.MeasureAsync(ContentRoot, Cancellation);

        Assert.Equal(new NetworkCacheShareUsage(0, 0), usage);
    }

    [Fact]
    public async Task CleanUpAsync_DeletesDownloadsAndImportedContentTheConfigurationNoLongerReferences()
    {
        WriteFile(Path.Combine(ContentRoot, "Downloads", "OperatingSystemImage", "abcd", "image.esd"), 10);
        ContentPackageReference keptDrivers = await ImportDriversAsync("kept");
        ContentPackageReference oldDrivers = await ImportDriversAsync("old");
        string keptDriversManifest = await PublishAsync(drivers, DriverLibrary.Library, keptDrivers);
        string oldDriversManifest = await PublishAsync(drivers, DriverLibrary.Library, oldDrivers);
        ContentPackageReference oldPackage = await ImportPostInstallationAsync("old");
        string oldPackageManifest = await PublishAsync(postInstallation, PostInstallationLibrary, oldPackage);
        string keptImageManifest = WriteImageGeneration(Hash('c'));
        string oldImageManifest = WriteImageGeneration(Hash('d'));
        FoundryConfigurationDocument configuration = new()
        {
            CustomDrivers = new CustomDriverSetsSettings { Sets = [new CustomDriverSetReference { Id = "1", Package = keptDrivers }] },
            CustomImages = new CustomImagesSettings { Images = [new CustomImageReference { Id = "1", ContentHash = Hash('C') }] }
        };

        NetworkCacheCleanUpResult result = await CleanUpLaterAsync(configuration, Cancellation);

        Assert.False(Directory.Exists(Path.Combine(ContentRoot, "Downloads")));
        Assert.Equal(1, result.RemovedImages);
        Assert.Equal(2, result.RemovedPackages);
        Assert.Equal(3, result.RemovedManifests);
        Assert.Empty(result.RetainedEntries);
        Assert.True(Directory.Exists(PackagePath(ContentPackageKind.Drivers, keptDrivers)));
        Assert.True(File.Exists(keptDriversManifest));
        Assert.False(Directory.Exists(PackagePath(ContentPackageKind.Drivers, oldDrivers)));
        Assert.False(File.Exists(oldDriversManifest));
        Assert.False(Directory.Exists(PackagePath(ContentPackageKind.PostInstallation, oldPackage)));
        Assert.False(File.Exists(oldPackageManifest));
        Assert.True(File.Exists(ImagePath(Hash('c'))));
        Assert.True(File.Exists(keptImageManifest));
        Assert.False(Directory.Exists(Path.GetDirectoryName(ImagePath(Hash('d')))));
        Assert.False(File.Exists(oldImageManifest));
    }

    [Fact]
    public async Task CleanUpAsync_KeepsAManifestThatStillDescribesKeptContent()
    {
        ContentPackageReference kept = await ImportDriversAsync("kept");
        ContentPackageReference old = await ImportDriversAsync("old");
        string manifest = await PublishAsync(drivers, DriverLibrary.Library, kept, old);
        string imageManifest = WriteImageGeneration(Hash('c'), Hash('d'));
        FoundryConfigurationDocument configuration = new()
        {
            CustomDrivers = new CustomDriverSetsSettings { Sets = [new CustomDriverSetReference { Id = "1", Package = kept }] },
            CustomImages = new CustomImagesSettings { Images = [new CustomImageReference { Id = "1", ContentHash = Hash('c') }] }
        };

        NetworkCacheCleanUpResult result = await CleanUpLaterAsync(configuration, Cancellation);

        Assert.Equal(1, result.RemovedImages);
        Assert.Equal(1, result.RemovedPackages);
        Assert.Equal(0, result.RemovedManifests);
        Assert.True(File.Exists(manifest));
        Assert.True(File.Exists(imageManifest));
        Assert.False(Directory.Exists(PackagePath(ContentPackageKind.Drivers, old)));
        Assert.False(File.Exists(ImagePath(Hash('d'))));
    }

    [Fact]
    public async Task CleanUpAsync_KeepsAndReportsEntriesNoManifestDescribes()
    {
        WriteImageGeneration(Hash('c'));
        string unknownImage = Path.Combine(ContentRoot, "Cache", "OperatingSystems", "Custom", Hash('e'), "image.wim");
        WriteFile(unknownImage, 4);
        string changedImage = ImagePath(Hash('c'));
        WriteFile(changedImage, 9);

        NetworkCacheCleanUpResult result = await CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation);

        Assert.Equal(0, result.RemovedImages);
        Assert.Equal(0, result.RemovedManifests);
        Assert.True(File.Exists(unknownImage));
        Assert.True(File.Exists(changedImage));
        Assert.Contains($"{ImagesRoot}/{Hash('e')}", result.RetainedEntries);
        Assert.Contains($"{ImagesRoot}/{Hash('c')}/image.wim", result.RetainedEntries);
    }

    [Fact]
    public async Task CleanUpAsync_KeepsAPackageADeviceStillReadsAndEmptiesTheOtherKinds()
    {
        ContentPackageReference oldDrivers = await ImportDriversAsync("old");
        string driversManifest = await PublishAsync(drivers, DriverLibrary.Library, oldDrivers);
        ContentPackageReference oldPackage = await ImportPostInstallationAsync("old");
        string packageManifest = await PublishAsync(postInstallation, PostInstallationLibrary, oldPackage);
        string inUse = Directory.EnumerateFiles(PackagePath(ContentPackageKind.Drivers, oldDrivers), "*.inf", SearchOption.AllDirectories).Single();

        NetworkCacheCleanUpResult result;
        using (new FileStream(inUse, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = await CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation);
        }

        Assert.True(File.Exists(inUse));
        Assert.True(File.Exists(driversManifest));
        Assert.Contains(result.RetainedEntries, entry => entry.EndsWith(".inf", StringComparison.Ordinal));
        Assert.False(Directory.Exists(PackagePath(ContentPackageKind.PostInstallation, oldPackage)));
        Assert.False(File.Exists(packageManifest));
    }

    [Fact]
    public async Task CleanUpAsync_DeletesStagingFoldersLeftByAnInterruptedPublication()
    {
        string[] staging =
        [
            Path.Combine(ContentRoot, ContentPackageKind.Drivers.MediaRoot.Replace('/', Path.DirectorySeparatorChar), ".pending-1"),
            Path.Combine(ContentRoot, ContentPackageKind.PostInstallation.MediaRoot.Replace('/', Path.DirectorySeparatorChar), ".pending-2"),
            Path.Combine(ContentRoot, "Cache", "OperatingSystems", "Custom", ".pending-3")
        ];
        foreach (string folder in staging) WriteFile(Path.Combine(folder, "sub", "file.bin"), 5);

        NetworkCacheCleanUpResult result = await CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation);

        Assert.All(staging, folder => Assert.False(Directory.Exists(folder)));
        Assert.Empty(result.RetainedEntries);
        Assert.Equal(0, (await NetworkCacheShareMaintenance.MeasureAsync(ContentRoot, Cancellation)).ImportedContentBytes);
    }

    [Fact]
    public async Task CleanUpAsync_RefusesAFoundryFolderThatIsARedirection()
    {
        string target = Path.Combine(root, "elsewhere");
        WriteFile(Path.Combine(target, "Downloads", "OperatingSystemImage", "abcd", "image.esd"), 10);
        Directory.CreateDirectory(Path.GetDirectoryName(ContentRoot)!);
        if (!TryCreateJunction(ContentRoot, target, out string failure)) Assert.Skip($"Junction creation is not available here: {failure}");

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation));

            Assert.True(File.Exists(Path.Combine(target, "Downloads", "OperatingSystemImage", "abcd", "image.esd")));
        }
        finally
        {
            Directory.Delete(ContentRoot);
        }
    }

    [Fact]
    public async Task CleanUpAsync_KeepsImportedContentThatAnotherKeysIndexBinds()
    {
        ContentPackageReference drivers1 = await ImportDriversAsync("indexed");
        ContentPackageReference old = await ImportDriversAsync("old");
        string driversManifest = await PublishAsync(drivers, DriverLibrary.Library, drivers1);
        string oldManifest = await PublishAsync(drivers, DriverLibrary.Library, old);
        ContentPackageReference package = await ImportPostInstallationAsync("indexed");
        string packageManifest = await PublishAsync(postInstallation, PostInstallationLibrary, package);
        string imageManifest = WriteImageGeneration(Hash('c'));
        string unboundImageManifest = WriteImageGeneration(Hash('d'));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await NetworkCacheIndexStore.WriteAsync(ContentRoot, other.KeyId, NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex
        {
            KeyId = other.KeyId,
            Sequence = 1,
            CustomImages = new DeployCustomImagesSettings { IsEnabled = true, ManifestId = ManifestId(imageManifest), ManifestHash = FileHash(imageManifest) },
            CustomDrivers = new DeployCustomDriversSettings { IsEnabled = true, ManifestId = ManifestId(driversManifest), ManifestHash = FileHash(driversManifest) },
            PreOobe = new DeployPreOobeSettings { IsEnabled = true, ManifestId = ManifestId(packageManifest), ManifestHash = FileHash(packageManifest) }
        }, other), Cancellation);

        NetworkCacheCleanUpResult result = await CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation);

        Assert.True(File.Exists(ImagePath(Hash('c'))));
        Assert.True(File.Exists(imageManifest));
        Assert.True(Directory.Exists(PackagePath(ContentPackageKind.Drivers, drivers1)));
        Assert.False(Directory.Exists(PackagePath(ContentPackageKind.Drivers, old)));
        Assert.False(File.Exists(oldManifest));
        Assert.True(File.Exists(driversManifest));
        Assert.True(Directory.Exists(PackagePath(ContentPackageKind.PostInstallation, package)));
        Assert.True(File.Exists(packageManifest));
        Assert.False(File.Exists(ImagePath(Hash('d'))));
        Assert.False(File.Exists(unboundImageManifest));
        Assert.Equal(1, result.RemovedImages);
        Assert.True(File.Exists(Path.Combine(ContentRoot, NetworkCacheIndexStore.FolderName, other.KeyId + ".json")));
    }

    [Fact]
    public async Task CleanUpAsync_StopsBeforeDeletingImportedContentWhenABoundManifestCannotBeRead()
    {
        ContentPackageReference indexed = await ImportDriversAsync("indexed");
        string manifest = await PublishAsync(drivers, DriverLibrary.Library, indexed);
        string image = WriteImageGeneration(Hash('c'));
        WriteFile(Path.Combine(ContentRoot, "Downloads", "OperatingSystemImage", "abcd", "image.esd"), 10);
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await NetworkCacheIndexStore.WriteAsync(ContentRoot, other.KeyId, NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex
        {
            KeyId = other.KeyId,
            Sequence = 1,
            CustomDrivers = new DeployCustomDriversSettings { IsEnabled = true, ManifestId = ManifestId(manifest) }
        }, other), Cancellation);

        using (new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() =>
                CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation));
        }

        Assert.True(Directory.Exists(PackagePath(ContentPackageKind.Drivers, indexed)));
        Assert.True(File.Exists(manifest));
        Assert.True(File.Exists(ImagePath(Hash('c'))));
        Assert.True(File.Exists(image));
        // The downloads go before the indexes are read under the publication locks.
        Assert.False(File.Exists(Path.Combine(ContentRoot, "Downloads", "OperatingSystemImage", "abcd", "image.esd")));
    }

    [Fact]
    public async Task CleanUpAsync_KeepsContentWhenAnIndexWritesTheManifestIdInUpperCase()
    {
        string imageManifest = WriteImageGeneration(Hash('c'));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await NetworkCacheIndexStore.WriteAsync(ContentRoot, other.KeyId, NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex
        {
            KeyId = other.KeyId,
            Sequence = 1,
            CustomImages = new DeployCustomImagesSettings { IsEnabled = true, ManifestId = ManifestId(imageManifest).ToUpperInvariant() }
        }, other), Cancellation);

        await CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation);

        Assert.True(File.Exists(ImagePath(Hash('c'))));
        Assert.True(File.Exists(imageManifest));
    }

    [Fact]
    public async Task CleanUpAsync_ReleasesContentOnceNoIndexBindsItAndIgnoresUnsafeManifestIds()
    {
        ContentPackageReference old = await ImportDriversAsync("old");
        string oldManifest = await PublishAsync(drivers, DriverLibrary.Library, old);
        WriteImageGeneration(Hash('c'));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await NetworkCacheIndexStore.WriteAsync(ContentRoot, other.KeyId, NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex
        {
            KeyId = other.KeyId,
            Sequence = 1,
            CustomImages = new DeployCustomImagesSettings { ManifestId = @"..\..\..\escape", ManifestHash = Hash('a') },
            CustomDrivers = new DeployCustomDriversSettings { ManifestId = "0123456789abcdef0123456789abcdef" },
            PreOobe = new DeployPreOobeSettings { ManifestId = null }
        }, other), Cancellation);

        NetworkCacheCleanUpResult result = await CleanUpLaterAsync(new FoundryConfigurationDocument(), Cancellation);

        Assert.Equal(1, result.RemovedImages);
        Assert.Equal(1, result.RemovedPackages);
        Assert.False(File.Exists(oldManifest));
    }

    [Fact]
    public async Task CleanUpAsync_DeletesIndexTemporaryFilesOlderThanADayAndKeepsNewerOnes()
    {
        string folder = Path.Combine(ContentRoot, NetworkCacheIndexStore.FolderName);
        string stale = Path.Combine(folder, "a.json.1234.tmp");
        string fresh = Path.Combine(folder, "b.json.5678.tmp");
        string final = Path.Combine(folder, "c.json");
        foreach (string path in new[] { stale, fresh, final }) WriteFile(path, 3);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-25));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddHours(-23));
        File.SetLastWriteTimeUtc(final, DateTime.UtcNow.AddDays(-30));

        NetworkCacheCleanUpResult result = await NetworkCacheShareMaintenance.CleanUpAsync(ContentRoot, new FoundryConfigurationDocument(), Cancellation);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(final));
        Assert.Empty(result.RetainedEntries);
    }

    [Fact]
    public async Task CleanUpAsync_KeepsContentPublishedWithItsIndexOnceTheDownloadsAreDeleted()
    {
        WriteFile(Path.Combine(ContentRoot, "Downloads", "OperatingSystemImage", "abcd", "image.esd"), 10);
        ContentPackageReference old = await ImportDriversAsync("old");
        string oldManifest = await PublishAsync(drivers, DriverLibrary.Library, old);
        ContentPackageReference published = await ImportDriversAsync("published");
        string? publishedManifest = null;
        using PublicationSigningKey other = PublicationSigningKey.Create();

        // Another Foundry OSD publishes new content and its index after Clean up deleted the downloads.
        NetworkCacheCleanUpResult result = await NetworkCacheShareMaintenance.CleanUpAsync(ContentRoot, new FoundryConfigurationDocument(), new LaterClock(),
            async token =>
            {
                publishedManifest = await PublishAsync(drivers, DriverLibrary.Library, published);
                await NetworkCacheIndexStore.WriteAsync(ContentRoot, other.KeyId, NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex
                {
                    KeyId = other.KeyId,
                    Sequence = 1,
                    CustomDrivers = new DeployCustomDriversSettings { IsEnabled = true, ManifestId = ManifestId(publishedManifest), ManifestHash = FileHash(publishedManifest) }
                }, other), token);
            }, Cancellation);

        Assert.False(Directory.Exists(Path.Combine(ContentRoot, "Downloads")));
        Assert.True(Directory.Exists(PackagePath(ContentPackageKind.Drivers, published)));
        Assert.True(File.Exists(publishedManifest));
        Assert.False(Directory.Exists(PackagePath(ContentPackageKind.Drivers, old)));
        Assert.False(File.Exists(oldManifest));
        Assert.Equal(1, result.RemovedPackages);
    }

    [Fact]
    public async Task CleanUpAsync_KeepsImportedContentWrittenWithinTheLastDay()
    {
        ContentPackageReference recent = await ImportDriversAsync("recent");
        string recentManifest = await PublishAsync(drivers, DriverLibrary.Library, recent);
        ContentPackageReference reused = await ImportDriversAsync("reused");
        string reusedManifest = await PublishAsync(drivers, DriverLibrary.Library, reused);
        // A new generation can reuse a package folder an older one wrote: its recent manifest still protects it.
        Age(new DirectoryInfo(PackagePath(ContentPackageKind.Drivers, reused)));
        string recentImageManifest = WriteImageGeneration(Hash('c'));
        string oldImageManifest = WriteImageGeneration(Hash('d'));
        Age(new DirectoryInfo(Path.GetDirectoryName(ImagePath(Hash('d')))!));
        Age(new FileInfo(oldImageManifest));

        NetworkCacheCleanUpResult result = await NetworkCacheShareMaintenance.CleanUpAsync(ContentRoot, new FoundryConfigurationDocument(), Cancellation);

        Assert.True(Directory.Exists(PackagePath(ContentPackageKind.Drivers, recent)));
        Assert.True(File.Exists(recentManifest));
        Assert.True(Directory.Exists(PackagePath(ContentPackageKind.Drivers, reused)));
        Assert.True(File.Exists(reusedManifest));
        Assert.True(File.Exists(ImagePath(Hash('c'))));
        Assert.True(File.Exists(recentImageManifest));
        Assert.False(Directory.Exists(Path.GetDirectoryName(ImagePath(Hash('d')))));
        Assert.False(File.Exists(oldImageManifest));
        Assert.Equal(new NetworkCacheCleanUpResult(1, 0, 1, []), result with { RetainedEntries = [] });
        Assert.Empty(result.RetainedEntries);
    }

    /// <summary>Cleans up as if two days had passed, so content the test just wrote is outside the publication grace period.</summary>
    private Task<NetworkCacheCleanUpResult> CleanUpLaterAsync(FoundryConfigurationDocument configuration, CancellationToken cancellationToken) =>
        NetworkCacheShareMaintenance.CleanUpAsync(ContentRoot, configuration, new LaterClock(), afterDownloadsDeleted: null, cancellationToken);

    /// <summary>Moves the last write of an entry, and of every entry under a folder, two days back.</summary>
    private static void Age(FileSystemInfo entry)
    {
        if (entry is DirectoryInfo directory)
            foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos()) Age(child);
        entry.LastWriteTimeUtc = DateTime.UtcNow.AddDays(-2);
    }

    /// <summary>A clock two days ahead of the real one.</summary>
    private sealed class LaterClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddDays(2);
    }

    private CustomDriverSetLibrary DriverLibrary => driverLibrary ??= new CustomDriverSetLibrary(Path.Combine(root, "drivers"));

    private ContentPackageLibraryService PostInstallationLibrary =>
        postInstallationLibrary ??= new ContentPackageLibraryService(Path.Combine(root, "packages"), ContentPackageKind.PostInstallation);

    private static string Hash(char character) => new(character, 64);

    private static string ManifestId(string manifestPath) => Path.GetFileNameWithoutExtension(manifestPath);

    private static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private string PackagePath(ContentPackageKind kind, ContentPackageReference reference) =>
        Path.Combine(ContentRoot, kind.MediaRoot.Replace('/', Path.DirectorySeparatorChar), "Packages", reference.ContentHash);

    private string ImagePath(string hash) => Path.Combine(ContentRoot, "Cache", "OperatingSystems", "Custom", hash, "image.wim");

    /// <summary>Publishes one media generation of the package kind to the share and returns the full path of its manifest.</summary>
    private async Task<string> PublishAsync(WinPeContentPackageMediaService service, ContentPackageLibraryService library, params ContentPackageReference[] references)
    {
        using WinPeContentPackageMediaLease lease = await service.PrepareAsync(library, references, Cancellation);
        await service.PublishAsync(lease, ContentRoot, Cancellation);
        return Path.Combine(ContentRoot, lease.ManifestRelativePath);
    }

    /// <summary>Writes custom images of 8 bytes each and the media manifest that describes them, as media publication lays them out.</summary>
    private string WriteImageGeneration(params string[] hashes)
    {
        foreach (string hash in hashes) WriteFile(ImagePath(hash), 8);
        string id = Guid.NewGuid().ToString("N");
        var manifest = new CustomImageMediaManifest
        {
            ManifestId = id,
            Images = hashes.Select(hash => new CustomImageMediaEntry
            {
                Reference = new CustomImageReference { Id = hash, ContentHash = hash.ToUpperInvariant(), Length = 8 },
                RelativePath = Path.Combine(CustomImageMediaPaths.RelativeRoot, hash, "image.wim")
            }).ToArray()
        };
        string path = Path.Combine(ContentRoot, "Cache", "OperatingSystems", "Custom", "manifests", id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(manifest, ConfigurationJsonDefaults.SerializerOptions));
        return path;
    }

    private async Task<ContentPackageReference> ImportDriversAsync(string name)
    {
        string source = Path.Combine(root, "sources", "drivers", name);
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, name + ".inf"), "[Version]\r\n" + name, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(source, "sub", name + ".sys"), "driver " + name, Cancellation);
        return await DriverLibrary.ImportAsync(source, Cancellation);
    }

    private async Task<ContentPackageReference> ImportPostInstallationAsync(string name)
    {
        string source = Path.Combine(root, "sources", "packages", name);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, name + ".cmd"), "echo " + name, Cancellation);
        return await PostInstallationLibrary.ImportAsync(source, Cancellation);
    }

    private static bool TryCreateJunction(string link, string target, out string failure)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        failure = $"{output} {error}".Trim();
        return process.ExitCode == 0;
    }

    private static void WriteFile(string path, int length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
    }
}
