// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class ContentPackageArchiveTests : IDisposable
{
    private readonly TemporaryDirectory directory = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Write_SamePackageTwice_ProducesIdenticalBytes()
    {
        ContentPackageLibraryService library = Library("source", ContentPackageKind.PostInstallation);
        ContentPackageReference reference = await ImportSourceAsync(library, ("setup.exe", "installer"), ("support/b.txt", "b"), ("support/A.txt", "a"));

        byte[] first = await WriteAsync(library, reference.ContentHash);
        byte[] second = await WriteAsync(library, reference.ContentHash);

        Assert.Equal(first, second);
        using ZipArchive archive = new(new MemoryStream(first), ZipArchiveMode.Read);
        Assert.Equal(["manifest.json", "files/setup.exe", "files/support/A.txt", "files/support/b.txt"], archive.Entries.Select(entry => entry.FullName));
        Assert.All(archive.Entries, entry => Assert.Equal(entry.Length, entry.CompressedLength));
    }

    [Fact]
    public async Task WriteImport_DriverSet_RoundTripsHashIntoDriversLibrary()
    {
        ContentPackageLibraryService source = Library("source", ContentPackageKind.Drivers);
        ContentPackageReference reference = await ImportSourceAsync(source, ("net/e1000.inf", "[Version]"), ("net/e1000.sys", "driver"));
        ContentPackageLibraryService target = Library("target", ContentPackageKind.Drivers);

        using MemoryStream archive = new(await WriteAsync(source, reference.ContentHash));
        ContentPackageReference imported = await ContentPackageArchive.ImportAsync(target, archive, reference.ContentHash, Cancellation);

        Assert.Equal(reference.ContentHash, imported.ContentHash);
        Assert.Equal(reference.FileCount, imported.FileCount);
        await using ContentPackageLease lease = await target.AcquireAsync(reference.ContentHash, Cancellation);
        Assert.Equal("driver", await File.ReadAllTextAsync(lease.Files.Single(file => file.RelativePath == "net/e1000.sys").SourcePath, Cancellation));
    }

    [Fact]
    public async Task Import_DriverSetWithoutInf_ThrowsAndLeavesLibraryUnchanged()
    {
        ContentPackageLibraryService source = Library("source", ContentPackageKind.PostInstallation);
        ContentPackageReference reference = await ImportSourceAsync(source, ("tool.exe", "tool"));
        ContentPackageLibraryService target = Library("target", ContentPackageKind.Drivers);

        using MemoryStream archive = new(await WriteAsync(source, reference.ContentHash));
        await Assert.ThrowsAsync<InvalidDataException>(() => ContentPackageArchive.ImportAsync(target, archive, reference.ContentHash, Cancellation));

        Assert.False(target.IsAvailable(reference));
        Assert.Empty(LibraryEntries("target"));
    }

    [Fact]
    public async Task Import_EntryWithParentSegment_Throws()
    {
        ContentPackageLibraryService source = Library("source", ContentPackageKind.PostInstallation);
        ContentPackageReference reference = await ImportSourceAsync(source, ("tool.exe", "tool"));
        using MemoryStream archive = new();
        archive.Write(await WriteAsync(source, reference.ContentHash));
        using (ZipArchive zip = new(archive, ZipArchiveMode.Update, leaveOpen: true))
        {
            await using StreamWriter writer = new(zip.CreateEntry("files/../evil.txt").Open());
            await writer.WriteAsync("evil");
        }

        archive.Position = 0;
        ContentPackageLibraryService target = Library("target", ContentPackageKind.PostInstallation);

        await Assert.ThrowsAsync<InvalidDataException>(() => ContentPackageArchive.ImportAsync(target, archive, reference.ContentHash, Cancellation));
        Assert.Empty(LibraryEntries("target"));
    }

    [Fact]
    public async Task Import_HashMismatch_Throws()
    {
        ContentPackageLibraryService source = Library("source", ContentPackageKind.PostInstallation);
        ContentPackageReference reference = await ImportSourceAsync(source, ("tool.exe", "tool"));
        ContentPackageReference other = await ImportSourceAsync(source, ("other.exe", "other"));
        ContentPackageLibraryService target = Library("target", ContentPackageKind.PostInstallation);

        using MemoryStream archive = new(await WriteAsync(source, reference.ContentHash));
        await Assert.ThrowsAsync<InvalidDataException>(() => ContentPackageArchive.ImportAsync(target, archive, other.ContentHash, Cancellation));

        Assert.False(target.IsAvailable(reference));
        Assert.Empty(LibraryEntries("target"));
    }

    public void Dispose()
    {
        directory.Dispose();
    }

    private ContentPackageLibraryService Library(string name, ContentPackageKind kind) => new(Path.Combine(directory.Path, name), kind);

    /// <summary>Lists library entries other than the lock file the library creates on first use.</summary>
    private IEnumerable<string> LibraryEntries(string name)
    {
        string root = Path.Combine(directory.Path, name);
        return Directory.Exists(root)
            ? Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".lock" && !IsEmptyDirectory(path))
            : [];
    }

    private static bool IsEmptyDirectory(string path) => Directory.Exists(path) && !Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();

    private async Task<ContentPackageReference> ImportSourceAsync(ContentPackageLibraryService library, params (string Path, string Text)[] files)
    {
        string source = Path.Combine(directory.Path, "input", Guid.NewGuid().ToString("N"));
        foreach ((string relativePath, string text) in files)
        {
            string path = Path.Combine(source, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, text, Cancellation);
        }

        return await library.ImportAsync(source, Cancellation);
    }

    private static async Task<byte[]> WriteAsync(ContentPackageLibraryService library, string hash)
    {
        using MemoryStream archive = new();
        await ContentPackageArchive.WriteAsync(library, hash, archive, Cancellation);
        return archive.ToArray();
    }
}
