// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Threading.Channels;
using Foundry.Core.Services.NetworkCache;
using Foundry.Utilities.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Cache;

/// <summary>One downloaded artifact to publish; <paramref name="Hashless"/> artifacts get a SHA-256 sidecar.</summary>
public sealed record NetworkCachePublication(string LocalPath, string Kind, string Key, string FileName, bool Hashless);

/// <summary>Publishes downloads to the network cache in the background, one at a time, without delaying deployment steps.</summary>
public interface INetworkCachePublisher
{
    /// <summary>Queues a publication; ignored when the session cannot write.</summary>
    void Enqueue(NetworkCachePublication publication);

    /// <summary>Waits up to <paramref name="limit"/> for queued publications, then cancels the rest. Never throws for share errors.</summary>
    Task DrainAsync(TimeSpan limit, CancellationToken cancellationToken);
}

public sealed class NetworkCachePublisher : INetworkCachePublisher
{
    private readonly INetworkCacheSessionAccessor accessor;
    private readonly ILogger<NetworkCachePublisher> logger;
    private readonly Func<NetworkCachePublication, string, CancellationToken, Task<NetworkCachePublishOutcome>> publish;
    private readonly Channel<NetworkCachePublication> queue = Channel.CreateUnbounded<NetworkCachePublication>(new() { SingleReader = true });
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private Task? consumer;

    public NetworkCachePublisher(INetworkCacheSessionAccessor accessor, ILogger<NetworkCachePublisher> logger)
        : this(accessor, logger, null)
    {
    }

    internal NetworkCachePublisher(
        INetworkCacheSessionAccessor accessor,
        ILogger<NetworkCachePublisher> logger,
        Func<NetworkCachePublication, string, CancellationToken, Task<NetworkCachePublishOutcome>>? publish)
    {
        this.accessor = accessor;
        this.logger = logger;
        this.publish = publish ?? PublishToShareAsync;
    }

    public void Enqueue(NetworkCachePublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (accessor.Current is not { IsAvailable: true, CanWrite: true } || !queue.Writer.TryWrite(publication))
        {
            return;
        }

        lock (gate)
        {
            consumer ??= Task.Run(ConsumeAsync);
        }
    }

    public async Task DrainAsync(TimeSpan limit, CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        Task? running;
        lock (gate)
        {
            running = consumer;
        }

        if (running is null) return;
        try
        {
            await running.WaitAsync(limit, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Network cache publication did not finish in time; remaining uploads are abandoned.");
            await stopping.CancelAsync().ConfigureAwait(false);
            await running.ConfigureAwait(false);
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (NetworkCachePublication item in queue.Reader.ReadAllAsync(stopping.Token).ConfigureAwait(false))
        {
            NetworkCacheSession? session = accessor.Current;
            if (session is not { IsAvailable: true, CanWrite: true, SharePath: not null }) continue;
            string directory = NetworkCacheLayout.DownloadDirectory(session.SharePath, item.Kind, item.Key);
            try
            {
                NetworkCachePublishOutcome outcome = await publish(item, directory, stopping.Token).ConfigureAwait(false);
                logger.LogInformation("Network cache publication finished. Kind={Kind}, FileName={FileName}, Outcome={Outcome}", item.Kind, item.FileName, outcome);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Network cache publication failed. Kind={Kind}, FileName={FileName}, Reason={Reason}", item.Kind, item.FileName, exception.GetType().Name);
            }
        }
    }

    private static Task<NetworkCachePublishOutcome> PublishToShareAsync(NetworkCachePublication item, string directory, CancellationToken cancellationToken) =>
        // The writer does synchronous share IO before its first await, so it always starts on the thread pool.
        Task.Run(async () =>
        {
            string? sidecar = item.Hashless ? await ComputeSha256Async(item.LocalPath, cancellationToken).ConfigureAwait(false) : null;
            return await NetworkCacheWriter.PublishFileAsync(item.LocalPath, directory, item.FileName,
                DiagnosticSessionContext.CurrentSessionId, sidecar, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 20, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
