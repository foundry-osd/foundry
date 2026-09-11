// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;

namespace Foundry.Telemetry.Tests;

public sealed class DurableLogQueueTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "foundry-log-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Restart_RetainsOriginalRecordAndAcknowledgementRemovesIt()
    {
        RemoteDiagnosticRecord record = CreateRecord("original");
        using (var queue = new DurableLogQueue(directory))
        {
            Assert.True(queue.Add(record));
        }
        using (var recovered = new DurableLogQueue(directory))
        {
            RemoteDiagnosticRecord replay = Assert.Single(recovered.Take(100));
            Assert.Equal(record.Timestamp, replay.Timestamp);
            Assert.Equal(record.Body, replay.Body);
            Assert.Equal(record.Attributes["diagnostics.record_id"].ToString(), replay.Attributes["diagnostics.record_id"].ToString());
            recovered.Remove([replay]);
        }
        using var empty = new DurableLogQueue(directory);
        Assert.Empty(empty.Take(100));
    }

    [Fact]
    public void ActiveProcessRecords_AreNotReplayedByAnotherProcess()
    {
        using var first = new DurableLogQueue(directory);
        first.Add(CreateRecord("first"));
        using var second = new DurableLogQueue(directory);
        Assert.Empty(second.Take(100));
        second.Add(CreateRecord("second"));
        Assert.Equal("first", Assert.Single(first.Take(100)).Body);
    }

    [Fact]
    public void Capacity_IsBoundedAndLossIsCounted()
    {
        using var queue = new DurableLogQueue(directory, maximumRecords: 2);
        queue.Add(CreateRecord("one"));
        queue.Add(CreateRecord("two"));
        queue.Add(CreateRecord("three"));
        Assert.Equal(["two", "three"], queue.Take(100).Select(record => record.Body));
        Assert.Equal(1, queue.DroppedCount);
    }

    [Fact]
    public void Revocation_RemovesPersistedPendingRecords()
    {
        using (var queue = new DurableLogQueue(directory))
        {
            queue.Add(CreateRecord("pending"));
            queue.Clear();
        }
        using var recovered = new DurableLogQueue(directory);
        Assert.Empty(recovered.Take(100));
    }

    [Fact]
    public void Revocation_WhenARecordCannotBeDeleted_DoesNotReplayItLater()
    {
        using (var queue = new DurableLogQueue(directory))
        {
            queue.Add(CreateRecord("revoked"));
            string path = Assert.Single(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
            File.SetAttributes(path, FileAttributes.ReadOnly);
            queue.Clear();
            Assert.True(queue.StorageFailureCount > 0);
            File.SetAttributes(path, FileAttributes.Normal);
            queue.Add(CreateRecord("after opt-in"));
        }
        using var recovered = new DurableLogQueue(directory);
        Assert.Equal("after opt-in", Assert.Single(recovered.Take(100)).Body);
    }

    [Fact]
    public void FailedPersistentStorage_DoesNotAcknowledgeImportedEvidence()
    {
        using var memory = new DurableLogQueue(null);
        Assert.False(memory.Add(CreateRecord("source must remain"), requireDurable: true));
        Assert.Single(memory.Take(100));
    }

    [Theory]
    [InlineData("acknowledge")]
    [InlineData("trim")]
    [InlineData("revoke")]
    public void UndeletablePayload_StillConsumesDiskBudget(string removal)
    {
        RemoteDiagnosticRecord original = CreateRecord("original");
        int budget = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original).Length;
        using var queue = new DurableLogQueue(directory, maximumRecords: 1, maximumBytes: budget);
        Assert.True(queue.Add(original));
        string path = Assert.Single(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (removal == "acknowledge") queue.Remove([original]);
            else if (removal == "revoke") queue.Clear();
            for (int index = 0; index < 3; index++)
            {
                Assert.False(queue.Add(CreateRecord("fallback"), requireDurable: true));
                Assert.Single(queue.Take(100));
                Assert.Equal(budget, PayloadBytes());
            }
            Assert.True(queue.StorageFailureCount > 0);
        }
        queue.Remove(queue.Take(100));
        Assert.True(queue.Add(CreateRecord("restored"), requireDurable: true));
        Assert.True(PayloadBytes() <= budget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recovery_ChargesUndeletableTemporaryFilesToDiskBudget(bool revoked)
    {
        string processDirectory = Path.Combine(directory, "abandoned");
        Directory.CreateDirectory(processDirectory);
        RemoteDiagnosticRecord record = CreateRecord("fallback");
        int budget = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(record).Length;
        string temporary = Path.Combine(processDirectory, "0000000000000000001-" + Guid.NewGuid().ToString("N") + ".json.tmp");
        File.WriteAllText(temporary, new string('{', budget));
        if (revoked) File.WriteAllText(Path.Combine(processDirectory, ".revoked"), "");
        using (var locked = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var queue = new DurableLogQueue(directory, maximumBytes: budget))
        {
            Assert.False(queue.Add(record, requireDurable: true));
            Assert.Single(queue.Take(100));
            Assert.Equal(budget, PayloadBytes());
        }
    }

    private long PayloadBytes() => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".json.tmp", StringComparison.Ordinal))
        .Sum(path => new FileInfo(path).Length);

    [Fact]
    public void Recovery_UndeletablePayloadStillConsumesRecordLimit()
    {
        using (var original = new DurableLogQueue(directory)) original.Add(CreateRecord("original"));
        string path = Assert.Single(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var recovered = new DurableLogQueue(directory, maximumRecords: 1);
        Assert.Equal("original", Assert.Single(recovered.Take(100)).Body);
        Assert.False(recovered.Add(CreateRecord("fallback"), requireDurable: true));
        Assert.Equal("fallback", Assert.Single(recovered.Take(100)).Body);
        Assert.Single(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void EmptyProcessDirectories_AreRemovedOnDisposal()
    {
        using (var queue = new DurableLogQueue(directory))
        {
            var record = CreateRecord("accepted");
            queue.Add(record);
            queue.Remove([record]);
        }
        Assert.Empty(Directory.EnumerateDirectories(directory));
    }

    [Fact]
    public void InterruptedAtomicWrite_RecoversCompleteRecordAndDiscardsPartialRecord()
    {
        string processDirectory = Path.Combine(directory, "abandoned");
        Directory.CreateDirectory(processDirectory);
        RemoteDiagnosticRecord record = CreateRecord("complete");
        string id = Guid.Parse(record.Attributes["diagnostics.record_id"].ToString()!).ToString("N");
        File.WriteAllText(Path.Combine(processDirectory, "0000000000000000001-" + id + ".json.tmp"),
            System.Text.Json.JsonSerializer.Serialize(record));
        File.WriteAllText(Path.Combine(processDirectory, "0000000000000000002-" + Guid.NewGuid().ToString("N") + ".json.tmp"), "{");
        using var recovered = new DurableLogQueue(directory);
        Assert.Equal("complete", Assert.Single(recovered.Take(100)).Body);
        Assert.Equal(1, recovered.DroppedCount);
        Assert.Empty(Directory.EnumerateFiles(processDirectory, "*.tmp"));
    }

    private static RemoteDiagnosticRecord CreateRecord(string body) => new(
        new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero), LogEventLevel.Debug, body,
        new Dictionary<string, object> { ["diagnostics.record_id"] = Guid.NewGuid().ToString(), ["service.name"] = "test" }, null);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
