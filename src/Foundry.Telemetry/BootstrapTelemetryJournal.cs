// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundry.Telemetry;

/// <summary>Analytics and exceptions retain independent handoff state; Log is retained for legacy journal migration.</summary>
internal enum BootstrapTelemetryDestination { Analytics, Log, Exception }

/// <summary>Buffered SDK handoff is deliberately distinct from an HTTP receipt.</summary>
internal enum BootstrapDeliveryState { Pending, HandedToTransport, Acknowledged }

/// <summary>A sanitized durable envelope; credentials are never stored in the journal.</summary>
internal sealed record BootstrapPendingRecord(
    Guid Id,
    string Scope,
    BootstrapTelemetryDestination Destination,
    DateTimeOffset Timestamp,
    Dictionary<string, object>? Properties,
    RemoteDiagnosticRecord? Diagnostic,
    int Attempts = 0,
    BootstrapDeliveryState State = BootstrapDeliveryState.Pending,
    bool RetainAcknowledgement = false);

/// <summary>Bounds both RAM and disk; callers serialize access and apply consent before replay.</summary>
internal sealed class BootstrapTelemetryJournal
{
    internal const int MaximumBytes = 5 * 1024 * 1024;
    private const int MaximumRecordBytes = 256 * 1024;
    private readonly string? _path;
    private readonly List<BootstrapPendingRecord> _records = [];

    internal BootstrapTelemetryJournal(string? path)
    {
        _path = path;
        Load();
    }

    internal IReadOnlyList<BootstrapPendingRecord> Records => _records;

    internal static string Scope(string host, string token, string installation) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', CanonicalDestination(host), token, installation))));

    private static string CanonicalDestination(string host)
    {
        if (!Uri.TryCreate(host, UriKind.Absolute, out Uri? uri)) return host.TrimEnd('/');
        string userInfo = uri.UserInfo.Length == 0 ? string.Empty : uri.UserInfo + "@";
        return uri.Scheme.ToLowerInvariant() + "://" + userInfo + uri.Authority.ToLowerInvariant() +
            uri.AbsolutePath.TrimEnd('/') + uri.Query + uri.Fragment;
    }

    internal void Add(BootstrapPendingRecord record)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(record).Length <= MaximumRecordBytes)
        {
            _records.Add(record);
            Save();
        }
    }

    /// <summary>Transfers all destinations in one durable update without replacing prior receipts or attempt counts.</summary>
    internal bool Import(IReadOnlyList<BootstrapPendingRecord> records)
    {
        if (records.Any(record => JsonSerializer.SerializeToUtf8Bytes(record).Length > MaximumRecordBytes)) return false;
        foreach (BootstrapPendingRecord record in records)
        {
            if (!_records.Any(existing => existing.Id == record.Id)) _records.Add(record);
        }
        return Save();
    }

    internal bool Update(BootstrapPendingRecord record)
    {
        int index = _records.FindIndex(candidate => candidate.Id == record.Id);
        if (index >= 0)
        {
            if (record.State == BootstrapDeliveryState.Acknowledged && !record.RetainAcknowledgement) _records.RemoveAt(index);
            else _records[index] = record;
            return Save();
        }
        return false;
    }

    internal void Purge(Func<BootstrapPendingRecord, bool> predicate)
    {
        _records.RemoveAll(record => predicate(record));
        Save();
    }

    private void Load()
    {
        if (_path is null) return;
        try
        {
            if (!File.Exists(_path)) return;
            // Reject oversized input before allocating; the next save replaces it with a bounded journal.
            if (new FileInfo(_path).Length > MaximumBytes) return;
            foreach (string line in File.ReadLines(_path))
            {
                if (Encoding.UTF8.GetByteCount(line) > MaximumRecordBytes) continue;
                try
                {
                    BootstrapPendingRecord? record = JsonSerializer.Deserialize<BootstrapPendingRecord>(line);
                    if (record is not null && record.Id != Guid.Empty && record.Scope is { Length: 64 } &&
                        Enum.IsDefined(record.Destination) && Enum.IsDefined(record.State) && record.Attempts >= 0 &&
                        (record.State != BootstrapDeliveryState.Acknowledged || record.RetainAcknowledgement) &&
                        (record.Destination == BootstrapTelemetryDestination.Analytics ? record.Properties is not null :
                            record.Diagnostic is { Attributes: not null, Body: not null } diagnostic && Enum.IsDefined(diagnostic.Level) &&
                            (record.Destination != BootstrapTelemetryDestination.Exception ||
                                diagnostic is { Exception: not null, Level: >= Serilog.Events.LogEventLevel.Error })) &&
                        !_records.Any(candidate => candidate.Id == record.Id))
                    {
                        _records.Add(Revalidate(Normalize(record)));
                    }
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine("Bootstrap telemetry journal could not be read.");
        }
    }

    private static BootstrapPendingRecord Revalidate(BootstrapPendingRecord record)
    {
        if (record.Destination == BootstrapTelemetryDestination.Log)
            return record with { Diagnostic = LogRecordFactory.SanitizePersistedRecord(record.Diagnostic!) };
        if (record.Destination != BootstrapTelemetryDestination.Analytics)
        {
            return record with { Diagnostic = RemoteDiagnosticPropertyPolicy.SanitizePersistedRecord(record.Diagnostic!) };
        }
        var candidates = record.Properties!.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        if (candidates.GetValueOrDefault("elapsed_seconds") is long seconds) candidates["elapsed_seconds"] = (double)seconds;
        if (candidates.GetValueOrDefault("child_exit_code") is long code && code is >= int.MinValue and <= int.MaxValue)
            candidates["child_exit_code"] = (int)code;
        var properties = TelemetryEventPropertyPolicy.Sanitize(TelemetryEvents.BootstrapFailed, candidates)
            .Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
        foreach (string name in new[] { "app_version", "build_configuration", "app_runtime", "app_runtime_architecture", "app_locale", "session_id" })
        {
            if (candidates.GetValueOrDefault(name) is string value) properties[name] = RemoteDiagnosticText.Sanitize(value, 128);
        }
        properties["app"] = TelemetryApps.FoundryBootstrap;
        properties["telemetry_schema_version"] = TelemetryDefaults.SchemaVersion;
        properties["$process_person_profile"] = false;
        properties["$geoip_disable"] = false;
        return record with { Properties = properties };
    }

    internal static RemoteDiagnosticRecord RevalidateDiagnostic(RemoteDiagnosticRecord record) =>
        RemoteDiagnosticPropertyPolicy.SanitizePersistedRecord(record with
        {
            Attributes = record.Attributes.ToDictionary(pair => pair.Key, pair => Scalar(pair.Value), StringComparer.Ordinal)
        });

    private static BootstrapPendingRecord Normalize(BootstrapPendingRecord record) => record with
    {
        Properties = record.Properties?.ToDictionary(pair => pair.Key, pair => Scalar(pair.Value), StringComparer.Ordinal),
        Diagnostic = record.Destination == BootstrapTelemetryDestination.Log ? record.Diagnostic : record.Diagnostic is { } diagnostic ? diagnostic with
        {
            Attributes = diagnostic.Attributes.ToDictionary(pair => pair.Key, pair => Scalar(pair.Value), StringComparer.Ordinal)
        } : null
    };

    private static object Scalar(object value) => value is JsonElement element ? element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when element.TryGetInt64(out long integer) => integer,
        JsonValueKind.Number when double.IsFinite(element.GetDouble()) => element.GetDouble(),
        JsonValueKind.Number => throw new FormatException("Non-finite journal number."),
        _ => string.Empty
    } : value;

    private bool Save()
    {
        var lines = _records.Select(record => JsonSerializer.Serialize(record)).ToList();
        long bytes = lines.Sum(line => (long)Encoding.UTF8.GetByteCount(line) + 1);
        while (bytes > MaximumBytes && lines.Count > 0)
        {
            bytes -= Encoding.UTF8.GetByteCount(lines[0]) + 1;
            lines.RemoveAt(0);
            _records.RemoveAt(0);
        }
        if (_path is null) return true;
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = _path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                {
                    writer.NewLine = "\n";
                    foreach (string line in lines) writer.WriteLine(line);
                }
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine("Bootstrap telemetry remains in memory because its journal could not be written.");
            return false;
        }
    }
}
