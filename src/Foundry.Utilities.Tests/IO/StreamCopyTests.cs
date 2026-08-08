// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.IO;

namespace Foundry.Utilities.Tests.IO;

public sealed class StreamCopyTests
{
    [Fact]
    public async Task CopyAsync_CopiesContentAndReportsCumulativeBytes()
    {
        byte[] content = new byte[200_000];
        Random.Shared.NextBytes(content);
        await using var source = new MemoryStream(content);
        await using var destination = new MemoryStream();
        var reports = new List<long>();

        long copied = await StreamCopy.CopyAsync(
            source,
            destination,
            reports.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal(content.Length, copied);
        Assert.Equal(content, destination.ToArray());
        Assert.NotEmpty(reports);
        Assert.Equal(content.Length, reports[^1]);
        Assert.True(reports.SequenceEqual(reports.Order()));
    }

    [Fact]
    public async Task CopyAsync_WithEmptySource_ReturnsZeroWithoutReporting()
    {
        await using var source = new MemoryStream();
        await using var destination = new MemoryStream();
        var reports = new List<long>();

        long copied = await StreamCopy.CopyAsync(
            source,
            destination,
            reports.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, copied);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task CopyAsync_WithPreCancelledToken_DoesNotWrite()
    {
        await using var source = new MemoryStream([1, 2, 3]);
        await using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StreamCopy.CopyAsync(source, destination, null, cancellation.Token));

        Assert.Equal(0, destination.Length);
    }

    [Fact]
    public async Task CopyAsync_WhenProgressCallbackThrows_PropagatesFailure()
    {
        await using var source = new MemoryStream([1, 2, 3]);
        await using var destination = new MemoryStream();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            StreamCopy.CopyAsync(
                source,
                destination,
                _ => throw new InvalidOperationException("callback failed"),
                TestContext.Current.CancellationToken));

        Assert.Equal("callback failed", exception.Message);
        Assert.Equal(3, destination.Length);
    }
}
