// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCachePublicationCheckTests : IDisposable
{
    private readonly string contentRoot = Path.Combine(Path.GetTempPath(), "foundry-ncpc-" + Guid.NewGuid().ToString("N"), NetworkCacheLayout.RootFolderName);
    private readonly PublicationSigningKey signer = PublicationSigningKey.Create();

    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        signer.Dispose();
        string parent = Path.GetDirectoryName(contentRoot)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    [Fact]
    public async Task IsIntactAsync_IsTrueWhenTheIndexAndItsContentAreThere()
    {
        await WriteIndexAsync(new NetworkCacheContentIndex { KeyId = signer.KeyId, Sequence = 3 });

        Assert.True(await NetworkCachePublicationCheck.IsIntactAsync(contentRoot, signer.PublicKey, 3, Cancellation));
    }

    [Fact]
    public async Task IsIntactAsync_IsFalseWithoutTheIndex()
    {
        Directory.CreateDirectory(contentRoot);

        Assert.False(await NetworkCachePublicationCheck.IsIntactAsync(contentRoot, signer.PublicKey, 1, Cancellation));
    }

    [Fact]
    public async Task IsIntactAsync_IsFalseWhenTheIndexIsOlderThanThePublication()
    {
        await WriteIndexAsync(new NetworkCacheContentIndex { KeyId = signer.KeyId, Sequence = 2 });

        Assert.False(await NetworkCachePublicationCheck.IsIntactAsync(contentRoot, signer.PublicKey, 3, Cancellation));
    }

    [Fact]
    public async Task IsIntactAsync_IsFalseWhenContentTheIndexBindsIsMissing()
    {
        await WriteIndexAsync(new NetworkCacheContentIndex
        {
            KeyId = signer.KeyId,
            Sequence = 1,
            CustomImages = new DeployCustomImagesSettings { IsEnabled = true, ManifestId = "images-1", ManifestHash = new string('a', 64) }
        });

        Assert.False(await NetworkCachePublicationCheck.IsIntactAsync(contentRoot, signer.PublicKey, 1, Cancellation));
    }

    private Task WriteIndexAsync(NetworkCacheContentIndex index) =>
        NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, NetworkCacheContentIndexCodec.Write(index, signer), Cancellation);
}
