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

        await Assert.ThrowsAsync<IOException>(() => service.PublishAsync(package, destination, TestContext.Current.CancellationToken));

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
    public async Task Publish_RejectsEscapingPayloadPath()
    {
        Directory.CreateDirectory(root);
        using WinPeCustomImageMediaLease package = CreatePackage("../outside.wim");
        var service = new WinPeCustomImageMediaService(_ => long.MaxValue, (_, _) => Task.FromResult<int?>(1));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PublishAsync(package, Path.Combine(root, "usb"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Prepare_RetainsIncludedInputsAndBindsExactManifestWithoutSourcePaths()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "input.wim");
        await File.WriteAllTextAsync(source, "imported image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference included = await library.ImportAsync(new(source, "Profile image"), cancellationToken: TestContext.Current.CancellationToken);
        CustomImageReference excluded = included with { Id = Guid.NewGuid().ToString("N"), ContentHash = new string('A', 64), IsIncluded = false };
        var service = new WinPeCustomImageMediaService();
        using WinPeCustomImageMediaLease package = await service.PrepareAsync(library,
            new() { IsEnabled = true, Images = [included, excluded] }, TestContext.Current.CancellationToken);
        CustomImageMediaManifest manifest = JsonSerializer.Deserialize<CustomImageMediaManifest>(package.ManifestBytes, ConfigurationJsonDefaults.SerializerOptions)!;

        Assert.Equal(included.Id, Assert.Single(manifest.Images).Reference.Id);
        Assert.Equal(4, Assert.Single(manifest.Images[0].Reference.Indexes).Index);
        Assert.DoesNotContain(root, Encoding.UTF8.GetString(package.ManifestBytes));
        Assert.Throws<IOException>(() => File.Open(package.Files[0].SourcePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
        string bound = WinPeCustomImageMediaService.BindConfiguration(package, """{"customImages":{"isEnabled":true}}""");
        WinPeCustomImageMediaService.ValidateConfigurationBinding(package, bound);
        Assert.Throws<InvalidDataException>(() => WinPeCustomImageMediaService.ValidateConfigurationBinding(package,
            bound.Replace(package.ManifestHash, new string('0', 64), StringComparison.Ordinal)));
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

    private WinPeCustomImageMediaLease CreatePackage(string? relativePath = null)
    {
        string source = Path.Combine(root, "source.wim");
        File.WriteAllText(source, "image");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("image")));
        return new WinPeCustomImageMediaLease("test-build", Encoding.UTF8.GetBytes("{\"manifestId\":\"test-build\"}"),
            [new WinPeCustomImageMediaFile(source, relativePath ?? Path.Combine("Cache", "OperatingSystems", "Custom", hash.ToLowerInvariant(), "image.wim"), 5, hash)], []);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
