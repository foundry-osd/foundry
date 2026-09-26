// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Images;
using Xunit;

namespace Foundry.Core.Tests.Images;

public sealed class CustomImagePreviewTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryCustomImagePreviewTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewLocksSourceAndReturnsExactIndexesWithoutCreatingLibrary()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        string libraryPath = Path.Combine(root, "library");
        var reader = new MetadataReader(path =>
        {
            Assert.Equal(source, path);
            Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            Assert.Throws<IOException>(() => File.Delete(path));
        });
        var library = new CustomImageLibraryService(libraryPath, reader);

        CustomImageImportPreview preview = await library.PreviewAsync(source, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, preview.Length);
        Assert.Equal([1, 4], preview.Indexes.Select(index => index.Index));
        Assert.False(Directory.Exists(libraryPath));
        File.Delete(source);
    }

    [Fact]
    public async Task CancelledPreviewReleasesSourceAndPublishesNothing()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader(_ => cancellation.Cancel()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.PreviewAsync(source, cancellationToken: cancellation.Token));

        File.Delete(source);
        Assert.False(Directory.Exists(Path.Combine(root, "library")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class MetadataReader(Action<string> inspect) : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
        {
            inspect(imagePath);
            return Task.FromResult<IReadOnlyList<CustomImageIndex>>([
                new() { Index = 1, Name = "Windows Pro", Architecture = "amd64" },
                new() { Index = 4, Name = "Windows Enterprise", Architecture = "arm64" }
            ]);
        }
    }
}
