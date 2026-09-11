// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog;

namespace Foundry.Telemetry;

/// <summary>
/// Persists logs before background delivery and retires them only after a terminal transport result.
/// Consent revocation discards pending data. Network operations never run on the logging thread.
/// </summary>
internal sealed class ReliableLogPipeline : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly DurableLogQueue queue;
    private readonly ILogBatchTransport transport;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly TimeSpan interval;
    private Task? worker;
    private bool enabled = true;
    private bool stopping;
    private bool disposed;
    private CancellationTokenSource? activeRequest;
    private int consentGeneration;
    private long rejected;
    private long reportedLosses;
    private long reportedStorageFailures;

    internal ReliableLogPipeline(ILogBatchTransport transport, string? directory, bool startDelivery = true,
        TimeSpan? interval = null)
    {
        this.transport = transport;
        queue = new DurableLogQueue(directory);
        this.interval = interval ?? TimeSpan.FromSeconds(2);
        if (startDelivery) StartDelivery();
    }

    internal int PendingCount { get { lock (gate) return queue.Count; } }
    internal IReadOnlyList<RemoteDiagnosticRecord> PendingRecords { get { lock (gate) return queue.Take(int.MaxValue); } }

    internal void Emit(RemoteDiagnosticRecord record)
    {
        lock (gate)
        {
            if (!enabled || stopping) return;
            queue.Add(record);
        }
    }

    /// <summary>Transfers recovered evidence only when it is safely accepted by this journal.</summary>
    internal bool TryImport(RemoteDiagnosticRecord record, bool requireDurable = true)
    {
        lock (gate) return enabled && !stopping && queue.Add(record, requireDurable);
    }

    internal void StartDelivery()
    {
        lock (gate) worker ??= Task.Run(DeliverAsync);
    }

    internal void Disable()
    {
        lock (gate)
        {
            enabled = false;
            consentGeneration++;
            activeRequest?.Cancel();
            queue.Clear();
        }
    }

    internal void Enable() { lock (gate) enabled = true; }

    private async Task DeliverAsync()
    {
        TimeSpan delay = interval;
        int failures = 0;
        int batchSize = 100;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await signal.WaitAsync(delay, lifetime.Token).ConfigureAwait(false);
                IReadOnlyList<RemoteDiagnosticRecord> batch;
                int generation;
                lock (gate)
                {
                    generation = consentGeneration;
                    batch = enabled ? queue.Take(batchSize) : [];
                    if (stopping && batch.Count == 0) break;
                }
                ReportLosses();
                if (batch.Count == 0) { delay = interval; continue; }
                LogBatchResult result;
                CancellationTokenSource? request = null;
                try
                {
                    Task<LogBatchResult> send;
                    lock (gate)
                    {
                        if (!enabled || generation != consentGeneration) continue;
                        request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        activeRequest = request;
                        request.CancelAfter(TimeSpan.FromSeconds(10));
                        send = transport.SendAsync(batch, request.Token);
                    }
                    result = await send.WaitAsync(request.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
                {
                    result = new LogBatchResult(LogBatchDisposition.Retry);
                }
#pragma warning disable CA1031 // An invalid log must not permanently stop unrelated records from being delivered.
                catch (Exception)
#pragma warning restore CA1031
                {
                    result = new LogBatchResult(batch.Count > 1 ? LogBatchDisposition.Split : LogBatchDisposition.Rejected, batch.Count);
                }
                finally
                {
                    lock (gate) activeRequest = null;
                    request?.Dispose();
                }
                lock (gate)
                {
                    if (result.Disposition is LogBatchDisposition.Accepted or LogBatchDisposition.Rejected)
                    {
                        queue.Remove(batch);
                        if (result.Disposition == LogBatchDisposition.Rejected)
                            rejected += Math.Clamp(result.RejectedRecordCount, 1, batch.Count);
                    }
                }
                if (result.Disposition == LogBatchDisposition.Split)
                {
                    batchSize = Math.Max(1, batch.Count / 2);
                    delay = TimeSpan.Zero;
                }
                else if (result.Disposition == LogBatchDisposition.Retry)
                {
                    failures = Math.Min(failures + 1, 6);
                    delay = result.RetryAfter ?? TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, failures)));
                    delay = TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 100, int.MaxValue));
                }
                else
                {
                    failures = 0;
                    delay = PendingCount >= 100 || stopping ? TimeSpan.Zero : interval;
                }
                ReportLosses();
                if (stopping && result.Disposition == LogBatchDisposition.Retry) break;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private void ReportLosses()
    {
        long losses;
        long storageFailures;
        lock (gate)
        {
            losses = queue.DroppedCount + rejected;
            storageFailures = queue.StorageFailureCount;
        }
        // Transport health is local-only: exporting an exporter failure would recursively generate logs.
        if (losses != reportedLosses || storageFailures != reportedStorageFailures)
        {
            Log.ForContext("PostHogTransportInternal", true).Warning(
                "PostHog log delivery discarded pending records or encountered storage failures. DiscardedRecords={DiscardedRecords}, StorageFailures={StorageFailures}",
                losses, storageFailures);
            reportedLosses = losses;
            reportedStorageFailures = storageFailures;
        }
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            stopping = true;
            worker ??= Task.Run(DeliverAsync);
            if (signal.CurrentCount == 0) signal.Release();
        }
        try { await worker.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stopping = true;
        }
        await lifetime.CancelAsync().ConfigureAwait(false);
        if (worker is not null) await worker.ConfigureAwait(false);
        transport.Dispose();
        queue.Dispose();
        signal.Dispose();
        lifetime.Dispose();
    }
}
