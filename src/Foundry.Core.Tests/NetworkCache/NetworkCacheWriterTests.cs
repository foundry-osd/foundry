// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCacheWriterTests : IDisposable
{
    private static readonly string SourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([1, 2, 3, 4]));
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-ncw-" + Guid.NewGuid().ToString("N"));
    private readonly string source;
    private readonly string target;

    public NetworkCacheWriterTests()
    {
        Directory.CreateDirectory(root);
        source = Path.Combine(root, "local.esd");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        target = Path.Combine(root, "share", "OperatingSystemImage", "abcd");
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task Publish_WritesPartialThenRenames()
    {
        NetworkCachePublishOutcome outcome = await NetworkCacheWriter.PublishFileAsync(source, target, "image.esd", "s1", SourceHash, 4, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkCachePublishOutcome.Published, outcome);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(Path.Combine(target, "image.esd")));
        Assert.Equal(["image.esd"], Directory.GetFiles(target).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Publish_WhenFinalExists_ReturnsAlreadyPresent()
    {
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "image.esd"), [9]);

        NetworkCachePublishOutcome outcome = await NetworkCacheWriter.PublishFileAsync(source, target, "image.esd", "s1", SourceHash, 4, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkCachePublishOutcome.AlreadyPresent, outcome);
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(target, "image.esd")));
        Assert.Single(Directory.GetFiles(target));
    }

    [Fact]
    public async Task Publish_WhenFinalAppearsDuringCopy_KeepsExistingAndDeletesPartial()
    {
        NetworkCachePublishOutcome outcome = await NetworkCacheWriter.PublishFileAsync(
            source, target, "image.esd", "s1", SourceHash, 4, TimeProvider.System, TestContext.Current.CancellationToken,
            beforeRename: () => File.WriteAllBytes(Path.Combine(target, "image.esd"), [7]));

        Assert.Equal(NetworkCachePublishOutcome.AlreadyPresent, outcome);
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(target, "image.esd")));
        Assert.Single(Directory.GetFiles(target));
    }

    [Fact]
    public async Task Publish_Hashless_WritesComputedSidecarBeforeRename()
    {
        NetworkCachePublishOutcome outcome = await NetworkCacheWriter.PublishFileAsync(source, target, "fw.cab", "s1", null, 4, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkCachePublishOutcome.Published, outcome);
        Assert.Equal(SourceHash.ToLowerInvariant(), File.ReadAllText(Path.Combine(target, "fw.cab.sha256")));
        Assert.True(File.Exists(Path.Combine(target, "fw.cab")));
    }

    [Fact]
    public async Task Publish_HashMismatch_ReturnsFailedAndPublishesNothing()
    {
        NetworkCachePublishOutcome outcome = await NetworkCacheWriter.PublishFileAsync(source, target, "image.esd", "s1", new string('A', 64), 4, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkCachePublishOutcome.Failed, outcome);
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public async Task Publish_HashlessSizeMismatch_ReturnsFailedAndPublishesNothing()
    {
        NetworkCachePublishOutcome outcome = await NetworkCacheWriter.PublishFileAsync(source, target, "fw.cab", "s1", null, 5, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.Equal(NetworkCachePublishOutcome.Failed, outcome);
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public async Task Publish_Cancelled_DeletesOwnPartial()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NetworkCacheWriter.PublishFileAsync(source, target, "image.esd", "s1", SourceHash, 4, TimeProvider.System, cancellation.Token));

        Assert.False(Directory.Exists(target) && Directory.EnumerateFiles(target).Any());
    }

    [Fact]
    public void DeleteOrphanPartials_RemovesOnlyOlderThan24Hours()
    {
        Directory.CreateDirectory(target);
        string old = Path.Combine(target, "image.esd.old.partial");
        string fresh = Path.Combine(target, "image.esd.fresh.partial");
        string final = Path.Combine(target, "image.esd");
        File.WriteAllBytes(old, [1]);
        File.WriteAllBytes(fresh, [1]);
        File.WriteAllBytes(final, [1]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-25));
        File.SetLastWriteTimeUtc(final, DateTime.UtcNow.AddHours(-48));

        NetworkCacheWriter.DeleteOrphanPartials(target, TimeProvider.System);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(final));
    }
}
