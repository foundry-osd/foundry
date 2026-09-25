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
        Assert.Equal(imported.Id, Assert.Single(await library.ListAsync(TestContext.Current.CancellationToken)).Id);
        Assert.True(File.Exists(lease.ImagePath));
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
        Assert.Empty(await library.ListAsync(TestContext.Current.CancellationToken));
        Assert.True(File.Exists(source));
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
    public async Task RefreshMetadataPersistsFullRevisionAndPreservesProfileAndLibraryIdentity()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        var reader = new MetadataReader { Version = "10.0.26100" };
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), reader);
        CustomImageReference imported = await library.ImportAsync(new(source, "Library image"), cancellationToken: TestContext.Current.CancellationToken);
        File.Delete(source);
        CustomImageReference profile = imported with { Id = "profile-image", DisplayName = "Profile image", IsIncluded = false };
        reader.Version = "10.0.26100.4652";

        CustomImageReference refreshed = await library.RefreshMetadataAsync(profile, TestContext.Current.CancellationToken);

        Assert.Equal(profile with { Indexes = refreshed.Indexes }, refreshed);
        Assert.All(refreshed.Indexes, index => Assert.Equal("10.0.26100.4652", index.Version));
        CustomImageReference persisted = Assert.Single(await library.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(imported with { Indexes = persisted.Indexes }, persisted);
        Assert.All(persisted.Indexes, index => Assert.Equal("10.0.26100.4652", index.Version));
    }

    [Fact]
    public async Task RefreshMetadataRejectsChangedContentLengthWithoutUpdatingLibrary()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        var reader = new MetadataReader { Version = "10.0.26100" };
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), reader);
        CustomImageReference imported = await library.ImportAsync(new(source, "Image"), cancellationToken: TestContext.Current.CancellationToken);
        string path;
        await using (CustomImageSourceLease lease = await library.AcquireAsync(imported, TestContext.Current.CancellationToken)) path = lease.ImagePath;
        await File.AppendAllTextAsync(path, "changed", TestContext.Current.CancellationToken);
        reader.Version = "10.0.26100.4652";

        await Assert.ThrowsAsync<InvalidDataException>(() => library.RefreshMetadataAsync(imported, TestContext.Current.CancellationToken));

        CustomImageReference persisted = Assert.Single(await library.ListAsync(TestContext.Current.CancellationToken));
        Assert.All(persisted.Indexes, index => Assert.Equal("10.0.26100", index.Version));
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
        public string? Version { get; set; }

        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomImageIndex>>([
                new() { Index = 1, Name = "First", EditionId = "UnknownEdition", Architecture = "x86", Version = Version },
                new() { Index = 4, Name = "Second", EditionId = "UnknownEdition", Architecture = "unknown", Version = Version }
            ]);
    }
}
