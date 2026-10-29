// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Threading.Channels;
using Foundry.Core.Services.NetworkCache;
using Foundry.Utilities.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Cache;

/// <summary>
/// One downloaded artifact to publish with the catalog hash and size it was verified against. Hashless artifacts
/// are published with a SHA-256 sidecar computed while copying.
/// </summary>
public sealed record NetworkCachePublication(string LocalPath, string Kind, string Key, string FileName, string? ExpectedHash, long? ExpectedSize);

/// <summary>Publishes downloads to the network cache in the background, one at a time, without delaying deployment steps.</summary>
public interface INetworkCachePublisher
{
    /// <summary>Queues a publication; ignored when the session cannot write or the queue was drained.</summary>
    void Enqueue(NetworkCachePublication publication);

    /// <summary>
    /// Waits up to <paramref name="limit"/> for queued publications, then cancels and abandons the rest after a short
    /// grace period. Never throws: publication is an optimization and must not fail a deployment.
    /// </summary>
    Task DrainAsync(TimeSpan limit, CancellationToken cancellationToken);

    /// <summary>Gets the upload in progress and the files still waiting, or <see langword="null"/> when nothing is being uploaded.</summary>
    NetworkCacheUploadStatus? CurrentUpload => null;
}

/// <summary>The file being uploaded to the network cache and how many queued files follow it.</summary>
/// <param name="FileName">The file name on the share.</param>
/// <param name="BytesCopied">The bytes copied so far.</param>
/// <param name="TotalBytes">The size of the file.</param>
/// <param name="FilesWaiting">The queued files after this one.</param>
public sealed record NetworkCacheUploadStatus(string FileName, long BytesCopied, long TotalBytes, int FilesWaiting);

public sealed class NetworkCachePublisher : INetworkCachePublisher
{
    private readonly INetworkCacheSessionAccessor accessor;
    private readonly ILogger<NetworkCachePublisher> logger;
    private readonly Func<NetworkCachePublication, string, CancellationToken, Task<NetworkCachePublishOutcome>> publish;
    private readonly TimeSpan abandonTimeout;
    private readonly Channel<NetworkCachePublication> queue = Channel.CreateUnbounded<NetworkCachePublication>(new() { SingleReader = true });
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private Task? consumer;
    private bool draining;
    private int queued;
    private NetworkCacheUploadStatus? current;
    private string? currentPartialPath;

    public NetworkCachePublisher(INetworkCacheSessionAccessor accessor, ILogger<NetworkCachePublisher> logger)
        : this(accessor, logger, null)
    {
    }

    internal NetworkCachePublisher(
        INetworkCacheSessionAccessor accessor,
        ILogger<NetworkCachePublisher> logger,
        Func<NetworkCachePublication, string, CancellationToken, Task<NetworkCachePublishOutcome>>? publish,
        TimeSpan? abandonTimeout = null)
    {
        this.accessor = accessor;
        this.logger = logger;
        this.publish = publish ?? PublishToShareAsync;
        this.abandonTimeout = abandonTimeout ?? TimeSpan.FromSeconds(10);
    }

    public void Enqueue(NetworkCachePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (accessor.Current is not { IsAvailable: true, CanWrite: true }) return;
        lock (gate)
        {
            if (draining) return;
            // Counted before the write: a consumer already waiting can take the item at once, and must not count below zero.
            Interlocked.Increment(ref queued);
            if (!queue.Writer.TryWrite(publication))
            {
                Interlocked.Decrement(ref queued);
                return;
            }

            consumer ??= Task.Run(ConsumeAsync);
        }
    }

    /// <inheritdoc />
    /// <remarks>The files waiting are counted when read, so a download queued during the upload is included.</remarks>
    public NetworkCacheUploadStatus? CurrentUpload =>
        Volatile.Read(ref current) is { } status ? status with { FilesWaiting = Math.Max(0, Volatile.Read(ref queued)) } : null;

    public async Task DrainAsync(TimeSpan limit, CancellationToken cancellationToken)
    {
        Task? running;
        lock (gate)
        {
            draining = true;
            queue.Writer.TryComplete();
            running = consumer;
        }

        if (running is null) return;
        logger.LogInformation("Waiting for network cache publications. LimitSeconds={LimitSeconds}", limit.TotalSeconds);
        try
        {
            await running.WaitAsync(limit, cancellationToken).ConfigureAwait(false);
            stopping.Dispose();
            logger.LogInformation("Network cache publications completed.");
        }
        catch (TimeoutException)
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            try
            {
                // Share calls that ignore cancellation can block for the SMB timeout; the deployment never waits for them.
                await running.WaitAsync(abandonTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
            }

            logger.LogWarning("Network cache publication did not finish in time; remaining uploads are abandoned.");
            // The share is disconnected next, so an upload still writing cannot delete its partial file; name it for the administrator.
            if (!running.IsCompleted && Volatile.Read(ref currentPartialPath) is { } partialPath)
            {
                logger.LogWarning("Network cache upload abandoned while writing; its temporary file may remain on the share. PartialPath={PartialPath}", partialPath);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Network cache publication stopped unexpectedly. Reason={Reason}", exception.GetType().Name);
        }
    }

    private async Task ConsumeAsync()
    {
        // The token is not passed to the reader: after cancellation, a completed channel must end the loop, not throw.
        await foreach (NetworkCachePublication item in queue.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref queued);
            if (stopping.IsCancellationRequested) return;
            NetworkCacheSession? session = accessor.Current;
            if (session is not { IsAvailable: true, CanWrite: true, SharePath: not null }) continue;
            string directory = NetworkCacheLayout.DownloadDirectory(session.SharePath, item.Kind, item.Key);
            long totalBytes = item.ExpectedSize ?? TryGetLength(item.LocalPath);
            Volatile.Write(ref current, new NetworkCacheUploadStatus(item.FileName, 0, totalBytes, 0));
            Volatile.Write(ref currentPartialPath, Path.Combine(directory, NetworkCacheLayout.PartialName(item.FileName, DiagnosticSessionContext.CurrentSessionId)));
            try
            {
                NetworkCachePublishOutcome outcome = await publish(item, directory, stopping.Token).ConfigureAwait(false);
                logger.LogInformation("Network cache publication finished. Kind={Kind}, FileName={FileName}, Outcome={Outcome}", item.Kind, item.FileName, outcome);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Network cache publication failed. Kind={Kind}, FileName={FileName}, Reason={Reason}", item.Kind, item.FileName, exception.GetType().Name);
            }
            finally
            {
                Volatile.Write(ref current, null);
                Volatile.Write(ref currentPartialPath, null);
            }
        }
    }

    private Task<NetworkCachePublishOutcome> PublishToShareAsync(NetworkCachePublication item, string directory, CancellationToken cancellationToken) =>
        // The writer does synchronous share IO before its first await, so it always starts on the thread pool.
        Task.Run(() => NetworkCacheWriter.PublishFileAsync(item.LocalPath, directory, item.FileName,
            DiagnosticSessionContext.CurrentSessionId, item.ExpectedHash, item.ExpectedSize, TimeProvider.System, cancellationToken,
            copied => Volatile.Write(ref current, Volatile.Read(ref current) is { } status ? status with { BytesCopied = copied } : null)), cancellationToken);

    private static long TryGetLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return 0; }
    }
}
