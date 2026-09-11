// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Foundry.Telemetry;

/// <summary>
/// Stores pending logs before transport. Each process leases its own directory; only abandoned
/// directories are recovered. Callers serialize access. A null directory provides bounded RAM storage.
/// </summary>
internal sealed class DurableLogQueue : IDisposable
{
    internal const int DefaultMaximumRecords = 4096;
    internal const long DefaultMaximumBytes = 50 * 1024 * 1024;
    private readonly int maximumRecords;
    private readonly long maximumBytes;
    private readonly List<Entry> entries = [];
    private readonly List<FileStream> leases = [];
    private readonly Dictionary<string, long> payloadFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> retiredFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? writerDirectory;
    private readonly string? rootDirectory;
    private long bytes;
    private long sequence;

    internal DurableLogQueue(string? directory, int maximumRecords = DefaultMaximumRecords,
        long maximumBytes = DefaultMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        this.maximumRecords = maximumRecords;
        this.maximumBytes = maximumBytes;
        rootDirectory = directory;
        if (directory is null) return;
        try
        {
            Directory.CreateDirectory(directory);
            RejectLinks(directory);
            foreach (string candidate in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            {
                if (!TryLease(candidate)) continue;
                foreach (string path in Directory.EnumerateFiles(candidate).Where(IsPayloadFile))
                    AccountExistingFile(path);
                if (File.Exists(Path.Combine(candidate, ".revoked")))
                {
                    foreach (string revoked in Directory.EnumerateFiles(candidate, "*.json")) Delete(revoked);
                    foreach (string revoked in Directory.EnumerateFiles(candidate, "*.json.tmp")) Delete(revoked);
                    continue;
                }
                RecoverTemporaryRecords(candidate);
                foreach (string path in Directory.EnumerateFiles(candidate, "*.json").Order(StringComparer.Ordinal))
                {
                    try
                    {
                        RejectLinks(path);
                        long size = new FileInfo(path).Length;
                        if (size > maximumBytes) { DropFile(path); continue; }
                        RemoteDiagnosticRecord? record = JsonSerializer.Deserialize<RemoteDiagnosticRecord>(File.ReadAllBytes(path));
                        if (record is null || record.Body is null || record.Attributes is null ||
                            !Enum.IsDefined(record.Level) || !TryId(record, out _))
                        {
                            DropFile(path);
                            continue;
                        }
                        entries.Add(new Entry(LogRecordFactory.SanitizePersistedRecord(record), path, size));
                        bytes += size;
                        Trim();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                    {
                        DropFile(path);
                    }
                }
            }
            CreateWriter();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            StorageFailureCount++;
            writerDirectory = null;
        }
    }

    internal long DroppedCount { get; private set; }
    internal long StorageFailureCount { get; private set; }
    internal int Count => entries.Count;

    /// <summary>Atomically persists the exact event snapshot, retaining a bounded RAM copy if storage fails.</summary>
    internal bool Add(RemoteDiagnosticRecord record, bool requireDurable = false)
    {
        if (!TryId(record, out Guid id)) throw new ArgumentException("A log requires a stable record ID.", nameof(record));
        Entry? existing = entries.Find(entry => Id(entry.Record) == id);
        if (existing is not null) return existing.Path is not null || (!requireDurable && rootDirectory is null);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(record);
        if (content.Length > maximumBytes) { DroppedCount++; return false; }
        // Reclaim the oldest pending record before creating the temporary file, so an atomic write
        // cannot exceed the physical budget even briefly. Failed deletes retain their disk charge.
        Trim(1, content.Length);
        string? path = null;
        if (writerDirectory is not null && CanPersist(content.Length))
        {
            path = Path.Combine(writerDirectory, (++sequence).ToString("D19", System.Globalization.CultureInfo.InvariantCulture) + "-" + id.ToString("N") + ".json");
            string temporary = path + ".tmp";
            payloadFiles.Add(temporary, content.Length);
            try
            {
                RejectLinks(writerDirectory);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path);
                payloadFiles.Remove(temporary);
                payloadFiles.Add(path, content.Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StorageFailureCount++;
                path = null;
            }
            finally
            {
                Delete(temporary);
            }
        }
        entries.Add(new Entry(record, path, content.Length));
        bytes += content.Length;
        return path is not null || (!requireDurable && rootDirectory is null);
    }

    internal IReadOnlyList<RemoteDiagnosticRecord> Take(int count) => entries.Take(count).Select(entry => entry.Record).ToArray();

    /// <summary>Retires acknowledged or explicitly rejected records; a failed delete remains replayable.</summary>
    internal void Remove(IReadOnlyList<RemoteDiagnosticRecord> records)
    {
        var ids = records.Select(Id).ToHashSet();
        for (int index = entries.Count - 1; index >= 0; index--)
        {
            if (!ids.Contains(Id(entries[index].Record))) continue;
            Delete(entries[index].Path);
            bytes -= entries[index].Bytes;
            entries.RemoveAt(index);
        }
    }

    /// <summary>Revocation deletes queued payloads, including recovered records.</summary>
    internal void Clear()
    {
        foreach (FileStream lease in leases)
        {
            try
            {
                string marker = Path.Combine(Path.GetDirectoryName(lease.Name)!, ".revoked");
                RejectLinks(marker);
                using var stream = new FileStream(marker, FileMode.Create, FileAccess.Write, FileShare.Read);
                stream.Flush(flushToDisk: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StorageFailureCount++; }
        }
        Remove(Take(int.MaxValue));
        foreach (string path in retiredFiles.ToArray()) Delete(path);
        writerDirectory = null;
        try { CreateWriter(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StorageFailureCount++; }
    }

    private void CreateWriter()
    {
        if (rootDirectory is null) return;
        string candidate = Path.Combine(rootDirectory, DateTime.UtcNow.Ticks.ToString("D19",
            System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        if (!TryLease(candidate)) throw new IOException("Cannot lease the log journal.");
        writerDirectory = candidate;
    }

    private void RecoverTemporaryRecords(string directory)
    {
        foreach (string temporary in Directory.EnumerateFiles(directory, "*.json.tmp"))
        {
            string name = Path.GetFileName(temporary);
            if (name.Length != 61 || name[19] != '-' || !name[..19].All(char.IsAsciiDigit) ||
                !Guid.TryParseExact(name.Substring(20, 32), "N", out Guid expectedId)) continue;
            try
            {
                RejectLinks(temporary);
                if (new FileInfo(temporary).Length > maximumBytes) { DropFile(temporary); continue; }
                RemoteDiagnosticRecord? record = JsonSerializer.Deserialize<RemoteDiagnosticRecord>(File.ReadAllBytes(temporary));
                if (record is null || record.Body is null || record.Attributes is null ||
                    !TryId(record, out Guid id) || id != expectedId) { DropFile(temporary); continue; }
                _ = LogRecordFactory.SanitizePersistedRecord(record);
                string target = temporary[..^4];
                if (File.Exists(target)) Delete(temporary);
                else
                {
                    File.Move(temporary, target);
                    long size = payloadFiles[temporary];
                    payloadFiles.Remove(temporary);
                    payloadFiles.Add(target, size);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                DropFile(temporary);
            }
        }
    }

    private void Trim(int incomingRecords = 0, long incomingBytes = 0)
    {
        while (entries.Count > maximumRecords - incomingRecords || bytes > maximumBytes - incomingBytes)
        {
            Entry entry = entries[0];
            Delete(entry.Path);
            bytes -= entry.Bytes;
            entries.RemoveAt(0);
            DroppedCount++;
        }
    }

    private void DropFile(string path) { Delete(path); DroppedCount++; }

    private void Delete(string? path)
    {
        if (path is null) return;
        try
        {
            RejectLinks(path);
            File.Delete(path);
            payloadFiles.Remove(path);
            retiredFiles.Remove(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            retiredFiles.Add(path);
            StorageFailureCount++;
        }
    }

    /// <summary>Charges inaccessible and abandoned payloads until their deletion actually succeeds.</summary>
    private void AccountExistingFile(string path)
    {
        try
        {
            RejectLinks(path);
            payloadFiles[path] = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unknown size must not create apparent free space for more persistent writes.
            payloadFiles[path] = maximumBytes;
            StorageFailureCount++;
        }
    }

    private bool CanPersist(int incomingBytes)
    {
        foreach (string path in retiredFiles.ToArray()) Delete(path);
        long available = maximumBytes;
        foreach (long size in payloadFiles.Values)
        {
            available -= Math.Min(available, size);
        }
        if (payloadFiles.Count < maximumRecords && available >= incomingBytes) return true;
        StorageFailureCount++;
        return false;
    }

    private static bool IsPayloadFile(string path) => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".json.tmp", StringComparison.OrdinalIgnoreCase);

    private bool TryLease(string directory)
    {
        try
        {
            RejectLinks(directory);
            string leasePath = Path.Combine(directory, ".lease");
            RejectLinks(leasePath);
            leases.Add(new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Log journal paths cannot contain links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static Guid Id(RemoteDiagnosticRecord record) => Guid.Parse(record.Attributes["diagnostics.record_id"].ToString()!);
    private static bool TryId(RemoteDiagnosticRecord record, out Guid id)
    {
        id = Guid.Empty;
        return record.Attributes.TryGetValue("diagnostics.record_id", out object? value) &&
            Guid.TryParse(value?.ToString(), out id) && id != Guid.Empty;
    }

    public void Dispose()
    {
        foreach (FileStream lease in leases)
        {
            string directory = Path.GetDirectoryName(lease.Name)!;
            lease.Dispose();
            try
            {
                if (!Directory.EnumerateFiles(directory).Any(IsPayloadFile))
                {
                    File.Delete(Path.Combine(directory, ".lease"));
                    File.Delete(Path.Combine(directory, ".revoked"));
                    // Nonrecursive: never remove unexpected files or another process's new records.
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        leases.Clear();
    }

    private sealed record Entry(RemoteDiagnosticRecord Record, string? Path, long Bytes);
}
