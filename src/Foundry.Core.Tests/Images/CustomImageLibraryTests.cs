// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
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
    public async Task OversizedLibraryMetadataFailsBeforePublishingContent()
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        string libraryPath = Path.Combine(root, "library");
        var reader = new MetadataReader
        {
            Indexes = Enumerable.Range(1, 600).Select(index => new CustomImageIndex
            {
                Index = index,
                Description = new string('a', 16384)
            }).ToArray()
        };
        var library = new CustomImageLibraryService(libraryPath, reader);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(new(source, "Image"),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(await library.ListAsync(TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(libraryPath, "content")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(libraryPath, "pending")));
        Assert.Equal("image", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedIndexCommitRemovesOnlyNewContent(bool contentDirectoryAlreadyExists)
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "first image", TestContext.Current.CancellationToken);
        string libraryPath = Path.Combine(root, "library");
        var library = new CustomImageLibraryService(libraryPath, new MetadataReader());
        CustomImageReference original = await library.ImportAsync(new(source, "First"), cancellationToken: TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(source, "second image", TestContext.Current.CancellationToken);
        string imageHash = Convert.ToHexStringLower(SHA256.HashData("second image"u8));
        string contentDirectory = Path.Combine(libraryPath, "content", imageHash);
        string retainedFile = Path.Combine(contentDirectory, "retained.txt");
        if (contentDirectoryAlreadyExists)
        {
            Directory.CreateDirectory(contentDirectory);
            await File.WriteAllTextAsync(retainedFile, "retained", TestContext.Current.CancellationToken);
        }

        using (FileStream indexLease = File.OpenRead(Path.Combine(libraryPath, "library.json")))
        {
            Exception? error = await Record.ExceptionAsync(() => library.ImportAsync(new(source, "Second"),
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }

        Assert.Equal(original.Id, Assert.Single(await library.ListAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Single(Directory.GetFiles(Path.Combine(libraryPath, "content"), "image.wim", SearchOption.AllDirectories));
        Assert.Equal(contentDirectoryAlreadyExists, Directory.Exists(contentDirectory));
        if (contentDirectoryAlreadyExists)
            Assert.Equal("retained", await File.ReadAllTextAsync(retainedFile, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetDirectories(Path.Combine(libraryPath, "pending")));
        await using CustomImageSourceLease lease = await library.AcquireAsync(original, TestContext.Current.CancellationToken);
        Assert.Equal("first image", await File.ReadAllTextAsync(lease.ImagePath, TestContext.Current.CancellationToken));
        Assert.Equal("second image", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("available")]
    [InlineData("missing")]
    [InlineData("corrupted")]
    public async Task FailedReimportIndexCommitPreservesReferencedContent(string contentState)
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        string libraryPath = Path.Combine(root, "library");
        var library = new CustomImageLibraryService(libraryPath, new MetadataReader());
        CustomImageReference original = await library.ImportAsync(new(source, "Original"), cancellationToken: TestContext.Current.CancellationToken);
        string imagePath;
        await using (CustomImageSourceLease lease = await library.AcquireAsync(original, TestContext.Current.CancellationToken)) imagePath = lease.ImagePath;
        if (contentState == "missing") File.Delete(imagePath);
        if (contentState == "corrupted") await File.WriteAllTextAsync(imagePath, "corrupt", TestContext.Current.CancellationToken);

        using (FileStream indexLease = File.OpenRead(Path.Combine(libraryPath, "library.json")))
        {
            Exception? error = await Record.ExceptionAsync(() => library.ImportAsync(new(source, "Renamed"),
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }

        CustomImageReference persisted = Assert.Single(await library.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(original.Id, persisted.Id);
        Assert.Equal("Original", persisted.DisplayName);
        await using CustomImageSourceLease retained = await library.AcquireAsync(original, TestContext.Current.CancellationToken);
        Assert.Equal("image", await File.ReadAllTextAsync(retained.ImagePath, TestContext.Current.CancellationToken));
        Assert.Equal("image", await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken));
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
        public IReadOnlyList<CustomImageIndex>? Indexes { get; init; }

        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomImageIndex>>(Indexes ?? [
                new() { Index = 1, Name = "First", EditionId = "UnknownEdition", Architecture = "x86", Version = Version },
                new() { Index = 4, Name = "Second", EditionId = "UnknownEdition", Architecture = "unknown", Version = Version }
            ]);
    }
}
