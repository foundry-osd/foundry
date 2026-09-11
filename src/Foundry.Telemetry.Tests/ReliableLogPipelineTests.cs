// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using Serilog.Events;

namespace Foundry.Telemetry.Tests;

public sealed class ReliableLogPipelineTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "foundry-delivery-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OfflineRestart_ReplaysAllLevelsWithOriginalIdsAndTimes()
    {
        var offline = new Transport(_ => new(LogBatchDisposition.Retry));
        var records = Enum.GetValues<LogEventLevel>().Select(level => Record(level)).ToArray();
        await using (var first = new ReliableLogPipeline(offline, directory, startDelivery: false))
        {
            foreach (var record in records) first.Emit(record);
            await first.FlushAsync(TestContext.Current.CancellationToken);
            Assert.Equal(6, first.PendingCount);
        }
        var online = new Transport(_ => new(LogBatchDisposition.Accepted));
        await using (var replay = new ReliableLogPipeline(online, directory, startDelivery: false))
        {
            await replay.FlushAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, replay.PendingCount);
        }
        RemoteDiagnosticRecord[] delivered = Assert.Single(online.Batches).ToArray();
        Assert.Equal(records.Select(r => r.Level), delivered.Select(r => r.Level));
        Assert.Equal(records.Select(r => r.Timestamp), delivered.Select(r => r.Timestamp));
        Assert.Equal(records.Select(Id), delivered.Select(Id));
    }

    [Fact]
    public async Task TransientFailure_RetriesTheSameRecordWithoutRestart()
    {
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        var transport = new Transport(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                return new(LogBatchDisposition.Retry, RetryAfter: TimeSpan.FromMilliseconds(100));
            accepted.TrySetResult();
            return new(LogBatchDisposition.Accepted);
        });
        await using var pipeline = new ReliableLogPipeline(transport, directory, interval: TimeSpan.FromMilliseconds(5));
        RemoteDiagnosticRecord record = Record(LogEventLevel.Debug);
        pipeline.Emit(record);
        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await pipeline.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, pipeline.PendingCount);
        Assert.Equal(2, transport.Batches.Count);
        Assert.All(transport.Batches, batch => Assert.Equal(Id(record), Id(Assert.Single(batch))));
    }

    [Fact]
    public async Task ShutdownDeadline_CancelsTransportAndPreservesPendingRecord()
    {
        var transport = new BlockingTransport();
        string id;
        await using (var pipeline = new ReliableLogPipeline(transport, directory, interval: TimeSpan.FromMilliseconds(5)))
        {
            RemoteDiagnosticRecord record = Record(LogEventLevel.Fatal);
            id = Id(record);
            pipeline.Emit(record);
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.FlushAsync(deadline.Token));
            await transport.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        using var recovered = new DurableLogQueue(directory);
        Assert.Equal(id, Id(Assert.Single(recovered.Take(10))));
    }

    [Fact]
    public async Task RepeatedMessages_AreNotSampledAndOversizedBatchesAreSplit()
    {
        var transport = new Transport(batch => batch.Count > 3 ? new(LogBatchDisposition.Split) : new(LogBatchDisposition.Accepted));
        await using var pipeline = new ReliableLogPipeline(transport, directory, startDelivery: false);
        for (int i = 0; i < 20; i++) pipeline.Emit(Record(LogEventLevel.Information));
        await pipeline.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, pipeline.PendingCount);
        Assert.Equal(20, transport.Batches.Where(batch => batch.Count <= 3).Sum(batch => batch.Count));
    }

    [Fact]
    public async Task Disable_CancelsInFlightRequestAndPurgesUnsentRecords()
    {
        var transport = new BlockingTransport();
        await using (var pipeline = new ReliableLogPipeline(transport, directory, interval: TimeSpan.FromMilliseconds(5)))
        {
            pipeline.Emit(Record(LogEventLevel.Debug));
            await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            pipeline.Disable();
            await transport.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, pipeline.PendingCount);
        }
        using var queue = new DurableLogQueue(directory);
        Assert.Empty(queue.Take(100));
    }

    [Fact]
    public async Task PartialRejection_DoesNotReplayTheAcceptedSubset()
    {
        var transport = new Transport(_ => new(LogBatchDisposition.Rejected, 1));
        await using var pipeline = new ReliableLogPipeline(transport, directory, startDelivery: false);
        pipeline.Emit(Record(LogEventLevel.Warning));
        pipeline.Emit(Record(LogEventLevel.Error));
        await pipeline.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, pipeline.PendingCount);
        Assert.Single(transport.Batches);
    }

    private static string Id(RemoteDiagnosticRecord record) => record.Attributes["diagnostics.record_id"].ToString()!;
    private static RemoteDiagnosticRecord Record(LogEventLevel level) => new(
        new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero), level, "Repeated operation",
        new Dictionary<string, object> { ["diagnostics.record_id"] = Guid.NewGuid().ToString(), ["service.name"] = "test" }, null);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private sealed class Transport(Func<IReadOnlyList<RemoteDiagnosticRecord>, LogBatchResult> respond) : ILogBatchTransport
    {
        internal ConcurrentQueue<IReadOnlyList<RemoteDiagnosticRecord>> Batches { get; } = new();
        public Task<LogBatchResult> SendAsync(IReadOnlyList<RemoteDiagnosticRecord> records, CancellationToken token)
        {
            Batches.Enqueue(records);
            return Task.FromResult(respond(records));
        }
        public void Dispose() { }
    }

    private sealed class BlockingTransport : ILogBatchTransport
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<LogBatchResult> SendAsync(IReadOnlyList<RemoteDiagnosticRecord> records, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            return new(LogBatchDisposition.Accepted);
        }
        public void Dispose() { }
    }
}
