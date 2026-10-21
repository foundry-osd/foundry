// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Tests.Packages;

public sealed class ContentPackageLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Fpkg", Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FolderSnapshotRetainsHierarchyEmptyFilesAndIndependentContent()
    {
        string source = CreateSource();
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        Directory.CreateDirectory(Path.Combine(source, "support"));
        await File.WriteAllTextAsync(Path.Combine(source, "install.msi"), "installer", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(source, "support", "Français 日本語.mst"), "transform", Cancellation);
        await File.WriteAllBytesAsync(Path.Combine(source, "support", "empty.cab"), [], Cancellation);
        var library = Library();
        var reference = await library.ImportAsync(source, Cancellation);
        Directory.Delete(source, recursive: true);
        using var lease = await library.AcquireAsync(reference.ContentHash, Cancellation);
        Assert.Equal(3, reference.FileCount);
        Assert.Equal(18, reference.Length);
        Assert.True(Directory.Exists(Path.Combine(lease.ContentDirectoryPath, "empty")));
        Assert.Contains(lease.Files, file => file.RelativePath == "support/Français 日本語.mst");
        Assert.Equal("transform", await File.ReadAllTextAsync(lease.Files.Single(file => file.RelativePath.EndsWith(".mst", StringComparison.Ordinal)).SourcePath, Cancellation));
        Assert.Throws<IOException>(() => File.Open(lease.Files[0].SourcePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
        await Assert.ThrowsAsync<IOException>(() => library.DeleteAsync(reference.ContentHash, [], Cancellation));
    }

    [Fact]
    public async Task SameContentDeduplicatesButRenamingChangesItsIdentity()
    {
        string source = CreateSource();
        await File.WriteAllTextAsync(Path.Combine(source, "script.ps1"), "exit 0", Cancellation);
        var library = Library();
        var first = await library.ImportAsync(source, Cancellation);
        var second = await library.ImportAsync(source, Cancellation);
        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Single(Directory.GetDirectories(Path.Combine(root, "library", "content")));
        File.Move(Path.Combine(source, "script.ps1"), Path.Combine(source, "renamed.ps1"));
        var renamed = await library.ImportAsync(source, Cancellation);
        Assert.NotEqual(first.ContentHash, renamed.ContentHash);
    }

    [Fact]
    public async Task AFileImportRetainsItsFileNameAndAReferenceCanBeRestored()
    {
        string source = CreateSource();
        string script = Path.Combine(source, "prepare.ps1");
        await File.WriteAllTextAsync(script, "exit 0", Cancellation);
        var library = Library();
        var reference = await library.ImportAsync(script, Cancellation);
        using (var lease = await library.AcquireAsync(reference.ContentHash, Cancellation))
            Assert.Equal("prepare.ps1", Assert.Single(lease.Files).RelativePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.DeleteAsync(reference.ContentHash, [reference.ContentHash.ToUpperInvariant()], Cancellation));
        await library.DeleteAsync(reference.ContentHash, [], Cancellation);
        Assert.False(library.IsAvailable(reference));
        var restored = await library.ImportAsync(script, Cancellation);
        Assert.Equal(reference.ContentHash, restored.ContentHash);
        Assert.True(library.IsAvailable(reference));
    }

    [Fact]
    public async Task CorruptedBytesCannotBeAcquiredButKnownUnreferencedContentCanBeRemoved()
    {
        string source = CreateSource();
        await File.WriteAllTextAsync(Path.Combine(source, "a.exe"), "original", Cancellation);
        var library = Library();
        var reference = await library.ImportAsync(source, Cancellation);
        string path;
        using (var lease = await library.AcquireAsync(reference.ContentHash, Cancellation)) path = lease.Files[0].SourcePath;
        await File.WriteAllTextAsync(path, "modified", Cancellation);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.AcquireAsync(reference.ContentHash, Cancellation));
        await library.DeleteAsync(reference.ContentHash, [], Cancellation);
        Assert.False(library.IsAvailable(reference));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReimportRepairsMissingOrCorruptedOwnedContent(bool missing)
    {
        string source = CreateSource();
        Directory.CreateDirectory(Path.Combine(source, "support"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.exe"), "original", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(source, "support", "data.txt"), "support", Cancellation);
        var library = Library();
        var reference = await library.ImportAsync(source, Cancellation);
        string damaged;
        using (var lease = await library.AcquireAsync(reference.ContentHash, Cancellation))
            damaged = lease.Files.Single(file => file.RelativePath == "a.exe").SourcePath;
        if (missing) File.Delete(damaged);
        else await File.WriteAllTextAsync(damaged, "damaged", Cancellation);

        var restored = await library.ImportAsync(source, Cancellation);

        Assert.Equal(reference.ContentHash, restored.ContentHash);
        using var repaired = await library.AcquireAsync(reference.ContentHash, Cancellation);
        Assert.Equal("original", await File.ReadAllTextAsync(damaged, Cancellation));
        Assert.Equal("support", await File.ReadAllTextAsync(repaired.Files.Single(file => file.RelativePath == "support/data.txt").SourcePath, Cancellation));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "library", "pending")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReimportDoesNotReplaceARevisionContainingUnmanagedFiles(bool outsideFilesDirectory)
    {
        string source = CreateSource();
        await File.WriteAllTextAsync(Path.Combine(source, "a.exe"), "original", Cancellation);
        var library = Library();
        var reference = await library.ImportAsync(source, Cancellation);
        string content;
        using (var lease = await library.AcquireAsync(reference.ContentHash, Cancellation)) content = lease.ContentDirectoryPath;
        File.Delete(Path.Combine(content, "a.exe"));
        string unmanaged = Path.Combine(outsideFilesDirectory ? Path.GetDirectoryName(content)! : content, "keep.txt");
        await File.WriteAllTextAsync(unmanaged, "operator data", Cancellation);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(source, Cancellation));

        Assert.Equal("operator data", await File.ReadAllTextAsync(unmanaged, Cancellation));
        Assert.False(File.Exists(Path.Combine(content, "a.exe")));
    }

    [Fact]
    public async Task ReimportDefersRepairUntilAnExistingLeaseIsReleased()
    {
        string source = CreateSource();
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.exe"), "original", Cancellation);
        var library = Library();
        var reference = await library.ImportAsync(source, Cancellation);
        string missingDirectory;
        using (var lease = await library.AcquireAsync(reference.ContentHash, Cancellation))
        {
            missingDirectory = Path.Combine(lease.ContentDirectoryPath, "empty");
            Directory.Delete(missingDirectory);

            await Assert.ThrowsAsync<IOException>(() => library.ImportAsync(source, Cancellation));

            Assert.False(Directory.Exists(missingDirectory));
            Assert.Equal("original", await File.ReadAllTextAsync(lease.Files[0].SourcePath, Cancellation));
            Assert.Throws<IOException>(() => File.Open(lease.Files[0].SourcePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
        }

        var restored = await library.ImportAsync(source, Cancellation);
        Assert.Equal(reference.ContentHash, restored.ContentHash);
        Assert.True(Directory.Exists(missingDirectory));
        using var repaired = await library.AcquireAsync(reference.ContentHash, Cancellation);
        Assert.Single(repaired.Files);
    }

    [Fact]
    public async Task UnmanagedFilesPreventDeletion()
    {
        string source = CreateSource();
        await File.WriteAllTextAsync(Path.Combine(source, "a.exe"), "original", Cancellation);
        var library = Library();
        var reference = await library.ImportAsync(source, Cancellation);
        string unexpected;
        using (var lease = await library.AcquireAsync(reference.ContentHash, Cancellation)) unexpected = Path.Combine(lease.ContentDirectoryPath, "keep.txt");
        await File.WriteAllTextAsync(unexpected, "operator data", Cancellation);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.DeleteAsync(reference.ContentHash, [], Cancellation));
        Assert.True(File.Exists(unexpected));
    }

    [Fact]
    public async Task SourceWriterPreventsImportAndPendingContentIsRemoved()
    {
        string source = CreateSource();
        string script = Path.Combine(source, "script.ps1");
        await File.WriteAllTextAsync(script, "exit 0", Cancellation);
        using var writer = new FileStream(script, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        await Assert.ThrowsAsync<IOException>(() => Library().ImportAsync(source, Cancellation));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "library", "pending")));
    }

    [Fact]
    public async Task CancellationDoesNotPublishContent()
    {
        string source = CreateSource();
        await File.WriteAllTextAsync(Path.Combine(source, "script.ps1"), "exit 0", Cancellation);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Library().ImportAsync(source, canceled.Token));
        Assert.False(Directory.Exists(Path.Combine(root, "library", "content")));
    }

    [Fact]
    public async Task RedirectedSourceIsRejected()
    {
        string source = CreateSource();
        string outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "script.ps1"), "exit 0", Cancellation);
        string link = Path.Combine(source, "redirect");
        try { Directory.CreateSymbolicLink(link, outside); }
        // Windows reports the missing symbolic link privilege as an IOException carrying ERROR_PRIVILEGE_NOT_HELD.
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070522)) { Assert.Skip("Symbolic link creation requires Windows developer mode or elevation."); }
        try { await Assert.ThrowsAsync<InvalidDataException>(() => Library().ImportAsync(source, Cancellation)); }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }
        Assert.True(File.Exists(Path.Combine(outside, "script.ps1")));
    }

    [Theory]
    [InlineData("../script.ps1")]
    [InlineData("C:/script.ps1")]
    [InlineData("script.ps1:secret")]
    [InlineData("CON.txt")]
    [InlineData("LPT1.exe")]
    [InlineData("a./script.ps1")]
    [InlineData("a /script.ps1")]
    [InlineData("a\\script.ps1")]
    public void UnsafePortablePathsAreRejected(string path) => Assert.Throws<InvalidDataException>(() => ContentPackagePathPolicy.PostInstallation.ValidateRelativePath(path));

    [Fact]
    public void CanonicalIdentityIsIndependentOfInputOrderButIncludesDirectories()
    {
        var a = new ContentPackageFile { RelativePath = "a", Length = 0, Sha256 = Convert.ToHexStringLower(SHA256.HashData([])) };
        var b = a with { RelativePath = "b" };
        var first = new ContentPackageManifest { Files = [a, b] };
        Assert.Equal(ContentPackageManifestCodec.GetContentHash(first), ContentPackageManifestCodec.GetContentHash(first with { Files = [b, a] }));
        Assert.NotEqual(ContentPackageManifestCodec.GetContentHash(first), ContentPackageManifestCodec.GetContentHash(first with { Directories = ["empty"] }));
        Assert.Throws<InvalidDataException>(() => ContentPackageManifestCodec.Serialize(first with { Files = [a, a with { RelativePath = "A" }] }));
    }

    [Fact]
    public async Task ImportRejectsFolderWithoutInfWhenKindRequiresIt()
    {
        string source = CreateSource();
        await File.WriteAllTextAsync(Path.Combine(source, "readme.txt"), "text", Cancellation);
        var library = Library(ContentPackageKind.PostInstallation with { RequiresInfFile = true });
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(source, Cancellation));
        Assert.Equal("ContentPackage.MissingInfFile", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "library", "content")));
    }

    [Fact]
    public async Task ImportAcceptsUppercaseInfExtension()
    {
        string source = CreateSource();
        await File.WriteAllTextAsync(Path.Combine(source, "NET.INF"), "[Version]", Cancellation);
        var library = Library(ContentPackageKind.PostInstallation with { RequiresInfFile = true });
        var reference = await library.ImportAsync(source, Cancellation);
        Assert.Equal(1, reference.FileCount);
    }

    [Fact]
    public void ManifestValidationRejectsMoreFilesThanTheKindAllows()
    {
        var file = new ContentPackageFile { RelativePath = "a", Length = 0, Sha256 = Convert.ToHexStringLower(SHA256.HashData([])) };
        var manifest = new ContentPackageManifest { Files = [file, file with { RelativePath = "b" }] };
        var kind = ContentPackageKind.PostInstallation with { MaximumFiles = 1 };
        Assert.Throws<InvalidDataException>(() => ContentPackageManifestCodec.Validate(manifest, kind));
        ContentPackageManifestCodec.Validate(manifest, ContentPackageKind.PostInstallation);
    }

    [Fact]
    public void DestinationPrefixIsRevalidated()
    {
        string longRoot = "C:\\" + new string('a', 245);
        Assert.Throws<InvalidDataException>(() => ContentPackagePathPolicy.PostInstallation.Resolve(longRoot, "long-file-name.exe"));
    }

    [Fact]
    public async Task DriversImportSucceedsWhenTheHostPathExceeds260Characters()
    {
        string source = CreateSource();
        string relativePath = "drivers/" + new string('a', 138) + ".inf";
        Assert.Equal(150, relativePath.Length);
        string sourceFile = Path.Combine(source, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "[Version]", Cancellation);
        string libraryRoot = Path.Combine(root, new string('n', 100), new string('m', 100), "library");
        var library = new ContentPackageLibraryService(libraryRoot, ContentPackageKind.Drivers);

        var reference = await library.ImportAsync(source, Cancellation);
        using var lease = await library.AcquireAsync(reference.ContentHash, Cancellation);

        Assert.True(Path.Combine(libraryRoot, "content", reference.ContentHash, "files", relativePath).Length > 260);
        Assert.Equal(9, lease.Files.Single().Length);
        Assert.Equal(9, new FileInfo(lease.Files.Single().SourcePath).Length);
        Assert.True(library.IsAvailable(reference));
    }

    [Theory]
    [InlineData(15_000, true, false)]
    [InlineData(20_000, true, false)]
    [InlineData(20_001, false, false)]
    [InlineData(10_000, true, true)]
    [InlineData(0, false, false)]
    public void ReferenceFileCountIsValidatedAgainstTheLibraryKind(int fileCount, bool drivers, bool postInstallation)
    {
        var reference = new ContentPackageReference { ContentHash = new string('a', 64), DisplayName = "Set", Length = 1, FileCount = fileCount };

        Assert.Equal(drivers, ContentPackageLibraryService.IsValidReference(reference, ContentPackageKind.Drivers));
        Assert.Equal(postInstallation, ContentPackageLibraryService.IsValidReference(reference, ContentPackageKind.PostInstallation));
    }

    private string CreateSource()
    {
        string path = Path.Combine(root, "source");
        Directory.CreateDirectory(path);
        return path;
    }
    private ContentPackageLibraryService Library(ContentPackageKind? kind = null) => new(Path.Combine(root, "library"), kind ?? ContentPackageKind.PostInstallation);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
