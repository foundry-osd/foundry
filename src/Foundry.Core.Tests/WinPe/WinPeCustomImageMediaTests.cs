// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeCustomImageMediaTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-images-media-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Publish_VerifiesAndReusesContentWithoutRemovingUnmanagedFiles()
    {
        Directory.CreateDirectory(root);
        using WinPeCustomImageMediaLease package = CreatePackage();
        string destination = Path.Combine(root, "usb");
        string manual = Path.Combine(destination, "Cache", "OperatingSystems", "Custom", "manual.wim");
        Directory.CreateDirectory(Path.GetDirectoryName(manual)!);
        await File.WriteAllTextAsync(manual, "operator image", TestContext.Current.CancellationToken);
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult<int?>(1));

        await service.PublishAsync(package, destination, TestContext.Current.CancellationToken);
        string image = Path.Combine(destination, package.Files[0].RelativePath);
        DateTime timestamp = File.GetLastWriteTimeUtc(image);
        await service.PublishAsync(package, destination, TestContext.Current.CancellationToken);

        Assert.Equal("image", await File.ReadAllTextAsync(image, TestContext.Current.CancellationToken));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(image));
        Assert.Equal("operator image", await File.ReadAllTextAsync(manual, TestContext.Current.CancellationToken));
        Assert.Equal(package.ManifestHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(destination, package.ManifestRelativePath), TestContext.Current.CancellationToken))));
    }

    [Fact]
    public async Task PublishToNetworkCache_KeepsAnImageOfTheRecordedLengthWithoutReadingItAndCopiesAMissingOne()
    {
        Directory.CreateDirectory(root);
        using WinPeCustomImageMediaLease package = CreatePackage();
        string share = Path.Combine(root, "share");
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult<int?>(1));

        await service.PublishToNetworkCacheAsync(package, share, TestContext.Current.CancellationToken);
        string image = Path.Combine(share, package.Files[0].RelativePath);
        Assert.Equal("image", await File.ReadAllTextAsync(image, TestContext.Current.CancellationToken));

        // Same length, other bytes: a share is never read back, so the copy stays; Foundry Deploy verifies it before use.
        await File.WriteAllTextAsync(image, "IMAGE", TestContext.Current.CancellationToken);
        await service.PublishToNetworkCacheAsync(package, share, TestContext.Current.CancellationToken);
        Assert.Equal("IMAGE", await File.ReadAllTextAsync(image, TestContext.Current.CancellationToken));

        // A local drive is read back, so the same file is replaced there.
        await service.PublishAsync(package, share, TestContext.Current.CancellationToken);
        Assert.Equal("image", await File.ReadAllTextAsync(image, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishToNetworkCache_ReportsACopyOnceAndMarksReusedContentAsJustPublished()
    {
        Directory.CreateDirectory(root);
        const int length = 4 * 1024 * 1024;
        using WinPeCustomImageMediaLease package = CreatePackage(content: new byte[length]);
        string share = Path.Combine(root, "share");
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult<int?>(1));
        var copying = new RecordingProgress();

        await service.PublishToNetworkCacheAsync(package, share, TestContext.Current.CancellationToken, copying);

        // A missing image is not counted when it is checked, only while it is copied, so progress never jumps ahead of the copy.
        Assert.Equal(copying.Reports.Select(report => report.BytesCompleted ?? 0).Order(), copying.Reports.Select(report => report.BytesCompleted ?? 0));
        Assert.Contains(copying.Reports, report => report.BytesCompleted == length);

        string imageFolder = Path.GetDirectoryName(Path.Combine(share, package.Files[0].RelativePath))!;
        string manifest = Path.Combine(share, package.ManifestRelativePath);
        DateTime old = DateTime.UtcNow.AddDays(-3);
        Directory.SetLastWriteTimeUtc(imageFolder, old);
        File.SetLastWriteTimeUtc(manifest, old);

        await service.PublishToNetworkCacheAsync(package, share, TestContext.Current.CancellationToken);

        // Reused content looks just published, so a clean up on another PC keeps it until the index that binds it is written.
        Assert.True(Directory.GetLastWriteTimeUtc(imageFolder) > old.AddDays(2));
        Assert.True(File.GetLastWriteTimeUtc(manifest) > old.AddDays(2));
    }

    [Fact]
    public async Task Publish_ReportsBytesInOrderUpToTheTotalWhenCopyingAndWhenReusingContent()
    {
        Directory.CreateDirectory(root);
        const int length = 8 * 1024 * 1024;
        using WinPeCustomImageMediaLease package = CreatePackage(content: new byte[length]);
        string destination = Path.Combine(root, "share");
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult<int?>(1));
        var copying = new RecordingProgress();
        var reusing = new RecordingProgress();

        await service.PublishAsync(package, destination, TestContext.Current.CancellationToken, copying);
        DateTime timestamp = File.GetLastWriteTimeUtc(Path.Combine(destination, package.Files[0].RelativePath));
        await service.PublishAsync(package, destination, TestContext.Current.CancellationToken, reusing);

        // Reports come at most every 100 ms, so a fast local copy may only report the end of the file.
        foreach (RecordingProgress progress in new[] { copying, reusing })
        {
            Assert.Contains(progress.Reports, report => report.BytesCompleted == length);
            Assert.All(progress.Reports.Where(report => report.TotalBytes is not null), report => Assert.Equal(length, report.TotalBytes));
            Assert.Equal(length, progress.Reports[^1].BytesCompleted);
            Assert.Equal(progress.Reports.Select(report => report.Percent).Order(), progress.Reports.Select(report => report.Percent));
        }
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(Path.Combine(destination, package.Files[0].RelativePath)));
    }

    [Fact]
    public async Task Publish_CorruptSourceDoesNotPublishManifestOrReplaceOldContent()
    {
        Directory.CreateDirectory(root);
        using WinPeCustomImageMediaLease package = CreatePackage();
        await File.WriteAllTextAsync(package.Files[0].SourcePath, "wrong", TestContext.Current.CancellationToken);
        string destination = Path.Combine(root, "usb");
        IReadOnlyDictionary<string, string> existingFiles = await SeedDestinationAsync(package, destination);
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult<int?>(1));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PublishAsync(package, destination, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(destination, package.ManifestRelativePath)));
        foreach ((string path, string content) in existingFiles)
            Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Publish_InsufficientSpacePreservesDestination()
    {
        Directory.CreateDirectory(root);
        using WinPeCustomImageMediaLease package = CreatePackage();
        string destination = Path.Combine(root, "usb");
        IReadOnlyDictionary<string, string> existingFiles = await SeedDestinationAsync(package, destination);
        var service = new WinPeCustomImageMediaService(_ => 0, (_, _) => Task.FromResult<int?>(1));

        await Assert.ThrowsAsync<WinPeMediaCapacityException>(() => service.PublishAsync(package, destination, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(destination, package.ManifestRelativePath)));
        foreach ((string path, string content) in existingFiles)
            Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(null)]
    public async Task SourceGuard_RejectsTargetDiskAndUnknownProvenance(int? sourceDisk)
    {
        Directory.CreateDirectory(root);
        using WinPeCustomImageMediaLease package = CreatePackage();
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult(sourceDisk));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ValidateInputDisksAsync(
            package.Files.Select(file => file.SourcePath), 3, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SourceGuard_ResolvesEachDriveRootOnce()
    {
        Directory.CreateDirectory(root);
        string[] paths = Enumerable.Range(0, 50).Select(index => Path.Combine(root, "driver-" + index + ".inf")).ToArray();
        var resolved = new List<string>();
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (path, _) =>
        {
            resolved.Add(path);
            return Task.FromResult<int?>(1);
        });

        await service.ValidateInputDisksAsync(paths.Append(@"Z:\foundry\a.wim").Append(@"z:\foundry\b.wim"), 3, TestContext.Current.CancellationToken);

        Assert.Equal(2, resolved.Count);
        Assert.Equal(1, resolved.Count(path => string.Equals(Path.GetPathRoot(path), Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, resolved.Count(path => string.Equals(Path.GetPathRoot(path), @"Z:\", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task SourceGuard_RejectsTargetDiskFoundOnALaterRoot()
    {
        Directory.CreateDirectory(root);
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue,
            (path, _) => Task.FromResult<int?>(path.StartsWith("Z:", StringComparison.OrdinalIgnoreCase) ? 3 : 1));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ValidateInputDisksAsync(
            [Path.Combine(root, "a.wim"), Path.Combine(root, "b.wim"), @"Z:\foundry\c.wim"], 3, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Publish_RejectsEscapingPayloadPath()
    {
        Directory.CreateDirectory(root);
        using WinPeCustomImageMediaLease package = CreatePackage("../outside.wim");
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult<int?>(1));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PublishAsync(package, Path.Combine(root, "usb"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(CustomImageSource.Catalog, false)]
    [InlineData(CustomImageSource.Catalog, true)]
    [InlineData(CustomImageSource.Custom, false)]
    [InlineData(CustomImageSource.Custom, true)]
    public async Task Prepare_RejectsEnabledImagesWithoutIncludedContent(CustomImageSource defaultSource, bool hasExcludedImage)
    {
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        var settings = new CustomImagesSettings
        {
            IsEnabled = true,
            DefaultSource = defaultSource,
            Images = hasExcludedImage ? [new()
            {
                Id = "excluded", DisplayName = "Excluded image", ContentHash = new string('a', 64), Length = 100,
                IsIncluded = false, Indexes = [new() { Index = 1 }]
            }] : []
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new WinPeCustomImageMediaService().PrepareAsync(
            library, settings, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(CustomImageSource.Catalog)]
    [InlineData(CustomImageSource.Custom)]
    public async Task Prepare_AllowsDisabledImagesWithoutContent(CustomImageSource defaultSource)
    {
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        using WinPeCustomImageMediaLease package = await new WinPeCustomImageMediaService().PrepareAsync(library,
            new() { DefaultSource = defaultSource }, TestContext.Current.CancellationToken);

        Assert.Empty(package.Files);
    }

    [Theory]
    [InlineData(CustomImageSource.Catalog)]
    [InlineData(CustomImageSource.Custom)]
    public async Task Prepare_RejectsMissingIncludedContentAndReleasesAcquiredInputs(CustomImageSource defaultSource)
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "input.wim");
        await File.WriteAllTextAsync(source, "imported image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference included = await library.ImportAsync(new(source, "Profile image"), cancellationToken: TestContext.Current.CancellationToken);
        CustomImageReference missing = included with { Id = "missing", ContentHash = new string('a', 64) };
        var settings = new CustomImagesSettings { IsEnabled = true, DefaultSource = defaultSource, Images = [included, missing] };

        await Assert.ThrowsAnyAsync<IOException>(() => new WinPeCustomImageMediaService().PrepareAsync(
            library, settings, TestContext.Current.CancellationToken));

        await library.DeleteAsync(included.ContentHash, TestContext.Current.CancellationToken);
        Assert.False(library.IsAvailable(included));
    }

    [Theory]
    [InlineData(CustomImageSource.Catalog)]
    [InlineData(CustomImageSource.Custom)]
    public async Task Prepare_RetainsIncludedInputsAndBindsExactManifestWithoutSourcePaths(CustomImageSource defaultSource)
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "input.wim");
        await File.WriteAllTextAsync(source, "imported image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference included = await library.ImportAsync(new(source, "Profile image"), cancellationToken: TestContext.Current.CancellationToken);
        CustomImageReference excluded = included with { Id = Guid.NewGuid().ToString("N"), ContentHash = new string('A', 64), IsIncluded = false };
        var service = new WinPeCustomImageMediaService();
        using WinPeCustomImageMediaLease package = await service.PrepareAsync(library,
            new() { IsEnabled = true, DefaultSource = defaultSource, Images = [included, excluded] }, TestContext.Current.CancellationToken);
        CustomImageMediaManifest manifest = JsonSerializer.Deserialize<CustomImageMediaManifest>(package.ManifestBytes, ConfigurationJsonDefaults.SerializerOptions)!;

        Assert.Equal(included.Id, Assert.Single(manifest.Images).Reference.Id);
        Assert.Equal(4, Assert.Single(manifest.Images[0].Reference.Indexes).Index);
        Assert.DoesNotContain(root, Encoding.UTF8.GetString(package.ManifestBytes));
        Assert.Throws<IOException>(() => File.Open(package.Files[0].SourcePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
        string bound = WinPeCustomImageMediaService.BindConfiguration(package, """{"authoringVersion":"26.10.3.1","customImages":{"isEnabled":true}}""");
        using JsonDocument boundJson = JsonDocument.Parse(bound);
        Assert.Equal("26.10.3.1", boundJson.RootElement.GetProperty("authoringVersion").GetString());
        WinPeCustomImageMediaService.ValidateConfigurationBinding(package, bound);
        Assert.Throws<InvalidDataException>(() => WinPeCustomImageMediaService.ValidateConfigurationBinding(package,
            bound.Replace(package.ManifestHash, new string('0', 64), StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PrepareTwice_WithTheSameContent_GivesTheSameManifestIdAndBytes()
    {
        (CustomImageLibraryService library, CustomImageReference image) = await ImportAsync("first image");
        var settings = new CustomImagesSettings { IsEnabled = true, Images = [image] };
        var service = new WinPeCustomImageMediaService();

        using WinPeCustomImageMediaLease first = await service.PrepareAsync(library, settings, TestContext.Current.CancellationToken);
        using WinPeCustomImageMediaLease second = await service.PrepareAsync(library, settings, TestContext.Current.CancellationToken);

        Assert.Equal(first.ManifestId, second.ManifestId);
        Assert.Equal(first.ManifestBytes, second.ManifestBytes);
        Assert.Equal(first.ManifestHash, second.ManifestHash);
        CustomImageMediaManifest manifest = JsonSerializer.Deserialize<CustomImageMediaManifest>(first.ManifestBytes, ConfigurationJsonDefaults.SerializerOptions)!;
        Assert.Equal(first.ManifestId, manifest.ManifestId);
        Assert.Matches("^[0-9a-f]{32}$", first.ManifestId);
    }

    [Fact]
    public async Task PrepareWithDifferentContent_GivesDifferentIds()
    {
        (CustomImageLibraryService library, CustomImageReference first) = await ImportAsync("first image");
        string otherSource = Path.Combine(root, "other.wim");
        await File.WriteAllTextAsync(otherSource, "second image", TestContext.Current.CancellationToken);
        CustomImageReference second = await library.ImportAsync(new(otherSource, "Other image"), cancellationToken: TestContext.Current.CancellationToken);
        var service = new WinPeCustomImageMediaService();

        using WinPeCustomImageMediaLease one = await service.PrepareAsync(library,
            new() { IsEnabled = true, Images = [first] }, TestContext.Current.CancellationToken);
        using WinPeCustomImageMediaLease both = await service.PrepareAsync(library,
            new() { IsEnabled = true, Images = [first, second] }, TestContext.Current.CancellationToken);

        Assert.NotEqual(one.ManifestId, both.ManifestId);
        Assert.NotEqual(one.ManifestHash, both.ManifestHash);
    }

    private async Task<(CustomImageLibraryService Library, CustomImageReference Image)> ImportAsync(string content)
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "input.wim");
        await File.WriteAllTextAsync(source, content, TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        return (library, await library.ImportAsync(new(source, "Profile image"), cancellationToken: TestContext.Current.CancellationToken));
    }

    private static async Task<IReadOnlyDictionary<string, string>> SeedDestinationAsync(WinPeCustomImageMediaLease package, string destination)
    {
        string customRoot = Path.Combine(destination, "Cache", "OperatingSystems", "Custom");
        var files = new Dictionary<string, string>
        {
            [Path.Combine(destination, package.Files[0].RelativePath)] = "previous image",
            [Path.Combine(customRoot, "manual.wim")] = "operator image",
            [Path.Combine(customRoot, "manifests", "previous-build.json")] = "{\"manifestId\":\"previous-build\"}"
        };
        foreach ((string path, string content) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        }
        return files;
    }

    private sealed class MetadataReader : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomImageIndex>>([new() { Index = 4, Name = "Any image", ExpandedSizeBytes = 1024 }]);
    }

    private WinPeCustomImageMediaLease CreatePackage(string? relativePath = null, byte[]? content = null)
    {
        string source = Path.Combine(root, "source.wim");
        content ??= Encoding.UTF8.GetBytes("image");
        File.WriteAllBytes(source, content);
        string hash = Convert.ToHexString(SHA256.HashData(content));
        return new WinPeCustomImageMediaLease("test-build", Encoding.UTF8.GetBytes("{\"manifestId\":\"test-build\"}"),
            [new WinPeCustomImageMediaFile(source, relativePath ?? Path.Combine("Cache", "OperatingSystems", "Custom", hash.ToLowerInvariant(), "image.wim"), content.Length, hash)], []);
    }

    /// <summary>Records reports synchronously, unlike <see cref="Progress{T}"/>, so their order is the reporting order.</summary>
    private sealed class RecordingProgress : IProgress<WinPeMediaProgress>
    {
        public List<WinPeMediaProgress> Reports { get; } = [];

        public void Report(WinPeMediaProgress value) => Reports.Add(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
