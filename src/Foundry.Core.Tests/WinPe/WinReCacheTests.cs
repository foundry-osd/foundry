// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinReCacheTests
{
    [Fact]
    public async Task PublishAsync_ThenFindValidAsync_ReturnsEntryForSameSource()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        var cache = new WinReCache(temp.Path);

        WinReCacheEntry published = await PublishAsync(cache, WinPeArchitecture.X64, "en-US", "AA11", "winre-a");
        WinReCacheEntry? found = await cache.FindValidAsync(WinPeArchitecture.X64, "en-us", "aa11", TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal(published.DirectoryPath, found.DirectoryPath);
        Assert.Equal(26300, found.Manifest.BuildMajor);
        Assert.Equal("winre-a", await File.ReadAllTextAsync(found.GetFilePath("winre.wim"), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetDirectories(temp.Path, ".pending-*"));
    }

    [Fact]
    public async Task FindValidAsync_WhenSourceHashDiffers_ReturnsNullButAnyValidEntryRemainsAvailable()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        var cache = new WinReCache(temp.Path);
        await PublishAsync(cache, WinPeArchitecture.Arm64, "fr-FR", "AA11", "winre-a");

        Assert.Null(await cache.FindValidAsync(WinPeArchitecture.Arm64, "fr-FR", "BB22", TestContext.Current.CancellationToken));
        Assert.Null(await cache.FindValidAsync(WinPeArchitecture.X64, "fr-FR", null, TestContext.Current.CancellationToken));
        WinReCacheEntry? previous = await cache.FindValidAsync(WinPeArchitecture.Arm64, "fr-FR", null, TestContext.Current.CancellationToken);
        Assert.Equal("AA11", previous?.Manifest.SourceSha256);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("edited")]
    [InlineData("missing-file")]
    [InlineData("missing-manifest")]
    [InlineData("invalid-manifest")]
    public async Task FindValidAsync_WhenEntryIsCorrupted_DeletesEntryAndReturnsNull(string corruption)
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        var cache = new WinReCache(temp.Path);
        WinReCacheEntry entry = await PublishAsync(cache, WinPeArchitecture.X64, "en-US", "AA11", "winre-a");
        string winRe = entry.GetFilePath("winre.wim");
        string manifest = Path.Combine(entry.DirectoryPath, "manifest.json");
        switch (corruption)
        {
            case "truncated": await File.WriteAllTextAsync(winRe, "w", TestContext.Current.CancellationToken); break;
            case "edited": await File.WriteAllTextAsync(winRe, "winre-b", TestContext.Current.CancellationToken); break;
            case "missing-file": File.Delete(Path.Combine(entry.DirectoryPath, "dmcmnutils.dll")); break;
            case "missing-manifest": File.Delete(manifest); break;
            default: await File.WriteAllTextAsync(manifest, "{", TestContext.Current.CancellationToken); break;
        }

        Assert.Null(await cache.FindValidAsync(WinPeArchitecture.X64, "en-US", null, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(entry.DirectoryPath));
    }

    [Fact]
    public async Task PublishAsync_RemovesPreviousEntryForSameArchitectureAndLanguageOnly()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        var cache = new WinReCache(temp.Path);
        WinReCacheEntry old = await PublishAsync(cache, WinPeArchitecture.X64, "en-US", "AA11", "old");
        WinReCacheEntry otherArchitecture = await PublishAsync(cache, WinPeArchitecture.Arm64, "en-US", "CC33", "arm");
        WinReCacheEntry otherLanguage = await PublishAsync(cache, WinPeArchitecture.X64, "fr-FR", "DD44", "fr");

        WinReCacheEntry current = await PublishAsync(cache, WinPeArchitecture.X64, "en-US", "BB22", "new");

        Assert.False(Directory.Exists(old.DirectoryPath));
        Assert.True(Directory.Exists(current.DirectoryPath));
        Assert.True(Directory.Exists(otherArchitecture.DirectoryPath));
        Assert.True(Directory.Exists(otherLanguage.DirectoryPath));
    }

    [Fact]
    public async Task FindValidAsync_ForShorterLanguageTag_KeepsEntriesOfLongerTag()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        var cache = new WinReCache(temp.Path);
        WinReCacheEntry serbian = await PublishAsync(cache, WinPeArchitecture.X64, "sr-Latn-RS", "AA11", "sr");

        Assert.Null(await cache.FindValidAsync(WinPeArchitecture.X64, "sr-Latn", null, TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(serbian.DirectoryPath));
    }

    [Fact]
    public async Task FindValidAsync_IgnoresUnpublishedPendingDirectories()
    {
        using var temp = new TestUtilities.TemporaryDirectory();
        var cache = new WinReCache(temp.Path);
        string pending = cache.CreatePendingDirectory();
        await File.WriteAllTextAsync(Path.Combine(pending, "winre.wim"), "partial", TestContext.Current.CancellationToken);

        Assert.Null(await cache.FindValidAsync(WinPeArchitecture.X64, "en-US", null, TestContext.Current.CancellationToken));
    }

    private static async Task<WinReCacheEntry> PublishAsync(WinReCache cache, WinPeArchitecture architecture, string language, string sourceSha256, string winReContent)
    {
        string pending = cache.CreatePendingDirectory();
        await File.WriteAllTextAsync(Path.Combine(pending, "winre.wim"), winReContent, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(pending, "dmcmnutils.dll"), "dm", TestContext.Current.CancellationToken);
        return await cache.PublishAsync(
            pending,
            new WinReCacheSource(architecture, language, 26300, 9711, "Pro", "source.esd", sourceSha256),
            ["winre.wim", "dmcmnutils.dll"],
            TestContext.Current.CancellationToken);
    }
}
