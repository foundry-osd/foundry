// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
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

    public void Dispose() => temp.Dispose();
}
