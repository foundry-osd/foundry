// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Bootstrap.Runtime;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class RuntimeArchiveCacheTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("FoundryArchiveCache-").FullName;

    [Fact]
    public async Task FailedPublicationKeepsThePreviousArchiveAndRemovesItsTemporaryFile()
    {
        string candidate = Path.Combine(root, "candidate.zip");
        string directory = Path.Combine(root, "cache");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "current.zip");
        File.WriteAllText(candidate, "verified replacement");
        File.WriteAllText(destination, "previous");
        await using (var locked = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Exception? failure = await Record.ExceptionAsync(() => RuntimeArchiveCache.StoreAsync(candidate, destination, TestContext.Current.CancellationToken));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        }

        Assert.Equal("previous", File.ReadAllText(destination));
        Assert.Equal([destination], Directory.GetFiles(directory));
    }

    [Fact]
    public async Task CancelledPublicationKeepsThePreviousArchive()
    {
        string source = Path.Combine(root, "candidate.zip");
        string destination = Path.Combine(root, "current.zip");
        File.WriteAllText(source, "verified replacement");
        File.WriteAllText(destination, "previous");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeArchiveCache.StoreAsync(source, destination, new CancellationToken(true)));

        Assert.Equal("previous", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(root, "*.download"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
