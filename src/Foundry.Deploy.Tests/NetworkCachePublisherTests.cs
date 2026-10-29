// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.NetworkCache;
using Foundry.Deploy.Services.Cache;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class NetworkCachePublisherTests : IDisposable
{
    private static readonly byte[] Bytes = [4, 3, 2, 1];
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-ncp-" + Guid.NewGuid().ToString("N"));
    private readonly string share;
    private readonly string local;
    private readonly NetworkCacheSessionAccessor accessor = new();

    public NetworkCachePublisherTests()
    {
        share = Path.Combine(root, "share");
        local = Path.Combine(root, "local", "fw.cab");
        Directory.CreateDirectory(share);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllBytes(local, Bytes);
        accessor.Current = NetworkCacheSession.Connected(share, canWrite: true, new NoopConnector());
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task Publisher_PublishesHashlessFileWithSidecar()
    {
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance);

        publisher.Enqueue(new(local, "MicrosoftUpdateCatalogFirmware", "u-key-4", "fw.cab", null, 4));
        await publisher.DrainAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        string final = Path.Combine(NetworkCacheLayout.DownloadDirectory(share, "MicrosoftUpdateCatalogFirmware", "u-key-4"), "fw.cab");
        Assert.Equal(Bytes, File.ReadAllBytes(final));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Bytes)), File.ReadAllText(final + NetworkCacheLayout.HashSidecarSuffix));
    }

    [Fact]
    public async Task CurrentUpload_NamesTheFileInProgressAndTheFilesWaiting()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance, async (_, _, token) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return NetworkCachePublishOutcome.Published;
        });
        Assert.Null(publisher.CurrentUpload);

        publisher.Enqueue(new(local, "OemDriverPack", "k1", "first.cab", null, 4));
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        publisher.Enqueue(new(local, "OemDriverPack", "k2", "second.cab", null, 4));
        NetworkCacheUploadStatus? upload = publisher.CurrentUpload;
        release.SetResult();
        await publisher.DrainAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("first.cab", upload?.FileName);
        Assert.Equal(4, upload?.TotalBytes);
        Assert.Equal(1, upload?.FilesWaiting);
        Assert.Null(publisher.CurrentUpload);
    }

    [Fact]
    public async Task Enqueue_ReadOnlySession_IsIgnored()
    {
        accessor.Current = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance);

        publisher.Enqueue(new(local, "OemDriverPack", "abcd", "fw.cab", null, 4));
        await publisher.DrainAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(Path.Combine(share, NetworkCacheLayout.RootFolderName, NetworkCacheLayout.DownloadsFolderName)));
    }

    [Fact]
    public async Task Publisher_WriteFailure_LogsAndContinues()
    {
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance);

        publisher.Enqueue(new(Path.Combine(root, "missing.cab"), "OemDriverPack", "a1", "missing.cab", null, 4));
        publisher.Enqueue(new(local, "OemDriverPack", "b2", "fw.cab", null, 4));
        await publisher.DrainAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(NetworkCacheLayout.DownloadDirectory(share, "OemDriverPack", "b2"), "fw.cab")));
    }

    [Fact]
    public async Task Drain_TimesOut_CancelsPendingPublication()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool cancelled = false;
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance, async (item, directory, token) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }

            return NetworkCachePublishOutcome.Published;
        });

        publisher.Enqueue(new(local, "OemDriverPack", "c3", "fw.cab", null, 4));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await publisher.DrainAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        Assert.True(cancelled);
    }

    [Fact]
    public async Task Drain_WhenPublicationIgnoresCancellation_DoesNotThrow()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance, async (item, directory, token) =>
        {
            started.TrySetResult();
            await Task.Delay(300, CancellationToken.None);
            return NetworkCachePublishOutcome.Published;
        }, abandonTimeout: TimeSpan.FromSeconds(10));

        publisher.Enqueue(new(local, "OemDriverPack", "d4", "fw.cab", null, 4));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await publisher.DrainAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Drain_UnexpectedPublicationError_DoesNotThrow()
    {
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance,
            (item, directory, token) => throw new InvalidOperationException("unexpected"));

        publisher.Enqueue(new(local, "OemDriverPack", "e5", "fw.cab", null, 4));

        await publisher.DrainAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Drain_WhenShareHangs_ReturnsAfterAbandonTimeout()
    {
        var publisher = new NetworkCachePublisher(accessor, NullLogger<NetworkCachePublisher>.Instance, async (item, directory, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            return NetworkCachePublishOutcome.Published;
        }, abandonTimeout: TimeSpan.FromMilliseconds(100));

        publisher.Enqueue(new(local, "OemDriverPack", "f6", "fw.cab", null, 4));

        await publisher.DrainAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Drain_WhenAnAbandonedUploadIsStillWriting_NamesItsTemporaryFile()
    {
        // The share is disconnected next, so the writer cannot delete its partial file; the log names it for the administrator.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new RecordingLogger();
        var publisher = new NetworkCachePublisher(accessor, logger, async (item, directory, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            return NetworkCachePublishOutcome.Published;
        }, abandonTimeout: TimeSpan.FromMilliseconds(50));

        publisher.Enqueue(new(local, "OemDriverPack", "g7", "fw.cab", null, 4));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await publisher.DrainAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        string expected = Path.Combine(NetworkCacheLayout.DownloadDirectory(share, "OemDriverPack", "g7"),
            NetworkCacheLayout.PartialName("fw.cab", DiagnosticSessionContext.CurrentSessionId));
        Assert.Contains(logger.Warnings, properties => Equals(properties.GetValueOrDefault("PartialPath"), expected));
    }

    private sealed class RecordingLogger : ILogger<NetworkCachePublisher>
    {
        public List<IReadOnlyDictionary<string, object?>> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning) return;
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            lock (Warnings) Warnings.Add(properties.ToDictionary(pair => pair.Key, pair => pair.Value));
        }
    }

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);

        public void Disconnect(string uncPath)
        {
        }
    }
}
