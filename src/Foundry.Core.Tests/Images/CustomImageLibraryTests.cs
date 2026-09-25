// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Images;
using Xunit;

namespace Foundry.Core.Tests.Images;

public sealed class CustomImageLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryCustomImagesTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ImportPreservesExactIndexesAndOwnsIndependentContent()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "test image data", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference imported = await library.ImportAsync(new(source, "My image"), cancellationToken: TestContext.Current.CancellationToken);
        File.Delete(source);

        await using CustomImageSourceLease lease = await library.AcquireAsync(imported, TestContext.Current.CancellationToken);
        Assert.Equal([1, 4], imported.Indexes.Select(index => index.Index));
        Assert.Equal("test image data", await File.ReadAllTextAsync(lease.ImagePath, TestContext.Current.CancellationToken));
        Assert.Throws<IOException>(() => File.Open(lease.ImagePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
        await Assert.ThrowsAsync<IOException>(() => library.DeleteAsync(imported.ContentHash, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AcquireRejectsChangedContentBeforeReturningLease()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "first image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference imported = await library.ImportAsync(new(source, "Image"), cancellationToken: TestContext.Current.CancellationToken);
        string path;
        await using (CustomImageSourceLease lease = await library.AcquireAsync(imported, TestContext.Current.CancellationToken)) path = lease.ImagePath;
        await File.WriteAllTextAsync(path, "other image", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.AcquireAsync(imported, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledImportPublishesNothing()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ImportAsync(new(source, "Image"), cancellationToken: cancellation.Token));
        Assert.Empty(await library.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AvailabilityDetectsMissingContentWithoutReadingImageMetadata()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference reference = await library.ImportAsync(new(source, "Image"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(library.IsAvailable(reference));
        await library.DeleteAsync(reference.ContentHash, TestContext.Current.CancellationToken);
        Assert.False(library.IsAvailable(reference));
    }

    [Fact]
    public async Task ReimportRepairsMissingContentAndPreservesReferenceIdentity()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference reference = await library.ImportAsync(new(source, "Image"), cancellationToken: TestContext.Current.CancellationToken);
        string path;
        await using (CustomImageSourceLease lease = await library.AcquireAsync(reference, TestContext.Current.CancellationToken)) path = lease.ImagePath;
        File.Delete(path);
        CustomImageReference repaired = await library.ImportAsync(new(source, "Restored image"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(reference.Id, repaired.Id);
        Assert.True(library.IsAvailable(reference));
        Assert.Single(await library.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SettingsKeepPreferredCustomImageIndependentFromDefaultSource()
    {
        var settings = new CustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Catalog, DefaultImageId = "missing" };
        Assert.NotEmpty(CustomImageSettingsValidator.Validate(settings));
        Assert.Empty(CustomImageSettingsValidator.Validate(settings with { DefaultImageId = null }));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class MetadataReader : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomImageIndex>>([
                new() { Index = 1, Name = "First", EditionId = "UnknownEdition", Architecture = "x86" },
                new() { Index = 4, Name = "Second", EditionId = "UnknownEdition", Architecture = "unknown" }
            ]);
    }
}
