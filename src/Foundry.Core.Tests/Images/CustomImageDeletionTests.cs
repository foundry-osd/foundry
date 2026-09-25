// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;

namespace Foundry.Core.Tests.Images;

public sealed class CustomImageDeletionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryImageDeletionTests", Guid.NewGuid().ToString("N"));
    private const string BundleHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task DeleteRemovesUnusedCompanionBundle()
    {
        var (library, image, companion) = await CreateLibraryAsync(sharedBundle: false);
        await library.DeleteAsync(image.ContentHash, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(companion));
        Assert.False(Directory.Exists(Path.Combine(root, "sources", BundleHash)));
        Assert.Empty(await library.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteRetainsBundleReferencedByAnotherImage()
    {
        var (library, image, companion) = await CreateLibraryAsync(sharedBundle: true);
        await library.DeleteAsync(image.ContentHash, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(companion));
        Assert.Single(await library.ListAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(root, "content", image.ContentHash, "image.wim")));
    }

    [Fact]
    public async Task LeasedCompanionPreventsAnyDeletionOrIndexChange()
    {
        var (library, image, companion) = await CreateLibraryAsync(sharedBundle: false);
        byte[] originalIndex = await File.ReadAllBytesAsync(Path.Combine(root, "library.json"), TestContext.Current.CancellationToken);
        using (var lease = new FileStream(companion, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(() => library.DeleteAsync(image.ContentHash, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(Path.Combine(root, "content", image.ContentHash, "image.wim")));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(companion)!, "first.cab")));
            Assert.Equal(originalIndex, await File.ReadAllBytesAsync(Path.Combine(root, "library.json"), TestContext.Current.CancellationToken));
        }
        await library.DeleteAsync(image.ContentHash, TestContext.Current.CancellationToken);
        Assert.False(File.Exists(companion));
    }

    private async Task<(CustomImageLibraryService Library, CustomImageReference Image, string Companion)> CreateLibraryAsync(bool sharedBundle)
    {
        Directory.CreateDirectory(root);
        var library = new CustomImageLibraryService(root, new MetadataReader());
        string source = Path.Combine(root, "input.wim");
        await File.WriteAllTextAsync(source, "first image", TestContext.Current.CancellationToken);
        CustomImageReference first = await library.ImportAsync(new(source, "First"), cancellationToken: TestContext.Current.CancellationToken);
        var entries = new List<CustomImageReference> { first with { SourceBundleHash = BundleHash } };
        if (sharedBundle)
        {
            await File.WriteAllTextAsync(source, "second image", TestContext.Current.CancellationToken);
            CustomImageReference second = await library.ImportAsync(new(source, "Second"), cancellationToken: TestContext.Current.CancellationToken);
            entries.Add(second with { SourceBundleHash = BundleHash });
        }
        string bundle = Path.Combine(root, "sources", BundleHash);
        string sxs = Path.Combine(bundle, "sxs");
        Directory.CreateDirectory(sxs);
        await File.WriteAllTextAsync(Path.Combine(bundle, "files.json"), "[]", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sxs, "first.cab"), "first", TestContext.Current.CancellationToken);
        string companion = Path.Combine(sxs, "second.cab");
        await File.WriteAllTextAsync(companion, "second", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(root, "library.json"), JsonSerializer.SerializeToUtf8Bytes(entries,
            ConfigurationJsonDefaults.SerializerOptions), TestContext.Current.CancellationToken);
        return (library, first, companion);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class MetadataReader : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomImageIndex>>([new() { Index = 1, Name = "Image", ExpandedSizeBytes = 4096 }]);
    }
}
