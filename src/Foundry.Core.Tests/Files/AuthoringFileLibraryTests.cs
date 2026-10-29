// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Files;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Files;

public sealed class AuthoringFileLibraryTests : IDisposable
{
    private readonly TemporaryDirectory temp = new();

    [Fact]
    public void Import_SameBytesTwice_ReturnsSameHashAndOneEntry()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        byte[] content = [1, 2, 3, 4];

        AuthoringFileReference first = library.Import(content, "profile.xml");
        AuthoringFileReference second = library.Import(content, "profile.xml");

        Assert.Equal(first, second);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), first.ContentHash);
        Assert.Equal(content.Length, first.Length);
        string directory = Assert.Single(Directory.GetDirectories(Path.Combine(temp.Path, "content")));
        Assert.Equal(first.ContentHash, Path.GetFileName(directory));
        Assert.Equal("profile.xml", Path.GetFileName(Assert.Single(Directory.GetFiles(directory))));
    }

    [Fact]
    public void Import_OverFourMiB_Throws()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        byte[] content = new byte[AuthoringFileLibrary.MaximumFileBytes + 1];

        Assert.Throws<InvalidDataException>(() => library.Import(content, "big.bin"));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "content")));
    }

    [Fact]
    public async Task ImportAsync_OverFourMiB_ThrowsWithoutStoring()
    {
        string source = Path.Combine(temp.Path, "big.bin");
        await File.WriteAllBytesAsync(source, new byte[AuthoringFileLibrary.MaximumFileBytes + 1], TestContext.Current.CancellationToken);
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "library"));

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(source, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "library", "content")));
    }

    [Fact]
    public async Task ImportAsync_CopiesFileAndKeepsItsName()
    {
        string source = Path.Combine(temp.Path, "wifi.xml");
        await File.WriteAllTextAsync(source, "<profile />", TestContext.Current.CancellationToken);
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "library"));

        AuthoringFileReference reference = await library.ImportAsync(source, TestContext.Current.CancellationToken);
        File.Delete(source);

        Assert.Equal("wifi.xml", reference.FileName);
        Assert.Equal("<profile />", Encoding.UTF8.GetString(library.ReadVerified(reference)));
    }

    [Fact]
    public void IsAvailable_DeletedContent_ReturnsFalse()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference reference = library.Import([9, 9], "a.bin");
        Assert.True(library.IsAvailable(reference));

        File.Delete(Path.Combine(temp.Path, "content", reference.ContentHash, reference.FileName));

        Assert.False(library.IsAvailable(reference));
    }

    [Fact]
    public void ReadVerified_TamperedContent_Throws()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference reference = library.Import([1, 2, 3], "a.bin");

        File.WriteAllBytes(Path.Combine(temp.Path, "content", reference.ContentHash, reference.FileName), [1, 2, 4]);

        Assert.Throws<InvalidDataException>(() => library.ReadVerified(reference));
    }

    [Fact]
    public void Import_FileNameWithPathSegments_StoresSanitizedName()
    {
        var library = new AuthoringFileLibrary(temp.Path);

        AuthoringFileReference reference = library.Import([1], @"..\..\outside/te:st?.xml");

        Assert.Equal("te_st_.xml", reference.FileName);
        Assert.True(File.Exists(Path.Combine(temp.Path, "content", reference.ContentHash, "te_st_.xml")));
        Assert.Empty(Directory.GetFiles(temp.Path, "*", SearchOption.TopDirectoryOnly));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b.xml")]
    [InlineData("CON.xml")]
    public void ReadVerified_InvalidReferenceName_ThrowsAndIsNotAvailable(string fileName)
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference reference = library.Import([1], "a.bin") with { FileName = fileName };

        Assert.False(library.IsAvailable(reference));
        Assert.Throws<InvalidDataException>(() => library.ReadVerified(reference));
    }

    [Fact]
    public void DeleteUnreferenced_UnreferencedFile_IsDeleted()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference removed = library.Import([1, 2], "answer.xml");
        long sequence = library.ImportSequence;

        AuthoringFileCleanupResult result = library.DeleteUnreferenced(new HashSet<string>(), sequence);

        Assert.Equal(new AuthoringFileCleanupResult(Removed: 1, Kept: 0, Failed: 0), result);
        Assert.False(library.IsAvailable(removed));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "content", removed.ContentHash)));
    }

    [Fact]
    public void DeleteUnreferenced_ReferencedFile_IsKept()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference kept = library.Import([1, 2], "certificate.pfx");
        AuthoringFileReference removed = library.Import([3, 4], "old.pfx");

        AuthoringFileCleanupResult result = library.DeleteUnreferenced(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { kept.ContentHash.ToUpperInvariant() }, library.ImportSequence);

        Assert.Equal(new AuthoringFileCleanupResult(Removed: 1, Kept: 1, Failed: 0), result);
        Assert.True(library.IsAvailable(kept));
        Assert.False(library.IsAvailable(removed));
    }

    [Fact]
    public void DeleteUnreferenced_LeasedFile_IsKeptUntilTheLeaseEnds()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference captured = library.Import([1, 2], "profile.xml");

        using (library.Lease([captured.ContentHash]))
        {
            Assert.Equal(new AuthoringFileCleanupResult(Removed: 0, Kept: 1, Failed: 0), library.DeleteUnreferenced(new HashSet<string>(), library.ImportSequence));
            Assert.True(library.IsAvailable(captured));
        }

        Assert.Equal(1, library.DeleteUnreferenced(new HashSet<string>(), library.ImportSequence).Removed);
        Assert.False(library.IsAvailable(captured));
    }

    [Fact]
    public void DeleteUnreferenced_HashStillReferencedByAnotherField_IsKept()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference certificate = library.Import([1, 2], "shared.pfx");
        var afterRemoval = new FoundryConfigurationDocument
        {
            // The 802.1X certificate was removed; Wi-Fi still uses the same bytes.
            Network = new NetworkSettings { Dot1x = new Dot1xSettings(), Wifi = new WifiSettings { ClientCertificate = certificate } }
        };

        AuthoringFileCleanupResult result = library.DeleteUnreferenced(ConfigurationContentReferences.AuthoringFileHashes(afterRemoval), library.ImportSequence);

        Assert.Equal(new AuthoringFileCleanupResult(Removed: 0, Kept: 1, Failed: 0), result);
        Assert.True(library.IsAvailable(certificate));
    }

    [Fact]
    public void DeleteUnreferenced_FileImportedAfterReferencesWereRead_IsKept()
    {
        var library = new AuthoringFileLibrary(temp.Path);
        AuthoringFileReference existing = library.Import([1, 2], "certificate.pfx");
        long sequence = library.ImportSequence;
        // Imported again by a page whose configuration save has not happened yet.
        library.Import([1, 2], "certificate.pfx");

        AuthoringFileCleanupResult result = library.DeleteUnreferenced(new HashSet<string>(), sequence);

        Assert.Equal(new AuthoringFileCleanupResult(Removed: 0, Kept: 1, Failed: 0), result);
        Assert.True(library.IsAvailable(existing));
    }

    [Fact]
    public void DeleteUnreferenced_MissingLibrary_RemovesNothing()
    {
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "missing"));

        Assert.Equal(new AuthoringFileCleanupResult(0, 0, 0), library.DeleteUnreferenced(new HashSet<string>(), library.ImportSequence));
    }

    public void Dispose() => temp.Dispose();
}
