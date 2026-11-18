// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Core;
using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>
/// Captures the guardian's records before connectivity, then drains them in the background once delivery starts.
/// A persistent journal is optional; a RAM-backed path does not survive a reboot.
/// </summary>
public sealed class BootstrapTelemetryPipeline : ILogEventSink, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly BootstrapTelemetryJournal _journal;
    private readonly PostHogRemoteDiagnosticsSink _capture;
    private readonly ReliableLogPipeline? _logs;
    private readonly TelemetryContext _context;
    private readonly string _usageScope;
    private readonly string _diagnosticScope;
    private readonly Func<IBootstrapTelemetryTransport> _transportFactory;
    private readonly TimeProvider _time;
    private readonly HashSet<Guid> _replayIds;
    private readonly HashSet<Guid> _previousBootIds;
    private readonly HashSet<Guid> _attempted = [];
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _deliveryCancellation = new();
    private readonly bool _usageEnabled;
    private readonly bool _diagnosticsEnabled;
    private bool _terminalCaptured;
    private bool _clockUsable;
    private bool _stopping;
    private Task? _worker;
    private Task? _shutdown;
    private int _replayed;
    private TimeSpan _replayElapsed;

    /// <summary>Creates capture without opening a remote connection. Null journalPath retains records only in memory.</summary>
    public BootstrapTelemetryPipeline(TelemetryOptions usage, TelemetryContext context,
        RemoteDiagnosticsOptions diagnostics, RemoteDiagnosticsContext diagnosticContext, string? journalPath)
        : this(usage, context, diagnostics, diagnosticContext, journalPath,
            () => new BootstrapTelemetryTransport(usage, context, diagnostics), TimeProvider.System)
    {
    }

    internal BootstrapTelemetryPipeline(TelemetryOptions usage, TelemetryContext context,
        RemoteDiagnosticsOptions diagnostics, RemoteDiagnosticsContext diagnosticContext, string? journalPath,
        Func<IBootstrapTelemetryTransport> transportFactory, TimeProvider timeProvider, ILogBatchTransport? logTransport = null)
    {
        _context = context;
        _transportFactory = transportFactory;
        _time = timeProvider;
        _usageEnabled = usage.CanSend && Uri.TryCreate(usage.HostUrl, UriKind.Absolute, out Uri? host) && host.Scheme == "https";
        _diagnosticsEnabled = diagnostics.CanSend;
        _usageScope = BootstrapTelemetryJournal.Scope(usage.HostUrl, usage.ProjectToken, usage.InstallId);
        _diagnosticScope = BootstrapTelemetryJournal.Scope(diagnostics.HostUrl, diagnostics.ProjectToken, diagnostics.InstallId);
        _journal = new BootstrapTelemetryJournal(journalPath);
        PurgeDisallowed();
        string? logDirectory = journalPath is null ? null : Path.Combine(journalPath + ".logs", _diagnosticScope);
        if (_diagnosticsEnabled)
        {
            _logs = new ReliableLogPipeline(logTransport ?? new OtlpLogTransport(diagnostics, diagnosticContext),
                logDirectory, startDelivery: false);
            TransferLegacyLogs();
        }
        else if (logDirectory is not null)
        {
            using var revoked = new DurableLogQueue(logDirectory);
            revoked.Clear();
        }
        _replayIds = _journal.Records.Select(record => record.Id).ToHashSet();
        _previousBootIds = new HashSet<Guid>(_replayIds);
        _capture = new PostHogRemoteDiagnosticsSink(static (_, _) => new CaptureOnlyExporter(),
            timeProvider: timeProvider, capture: CaptureDiagnostic, logCapture: CaptureLog);
        _capture.Configure(diagnostics, diagnosticContext);
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent) => _capture.Emit(logEvent);

    /// <summary>Records the single terminal failure event; warnings, cancellation and success never call this method.</summary>
    public void CaptureTerminalFailure(IReadOnlyDictionary<string, object?> properties)
    {
        lock (_gate)
        {
            if (_stopping || _terminalCaptured) return;
            _terminalCaptured = true;
            if (!_usageEnabled) return;
            Dictionary<string, object> envelope = PostHogTelemetryService.BuildProperties(
                _context, TelemetryEvents.BootstrapFailed, properties);
            _journal.Add(new BootstrapPendingRecord(Guid.NewGuid(), _usageScope,
                BootstrapTelemetryDestination.Analytics, _time.GetUtcNow(), envelope, null));
            Signal();
        }
    }

    /// <summary>Enables nonterminal delivery; a clock that is not usable defers age-based expiry.</summary>
    public void StartDelivery(bool clockUsable)
    {
        lock (_gate)
        {
            if (_stopping) return;
            _clockUsable |= clockUsable;
            Expire();
            _logs?.StartDelivery();
            StartWorker();
        }
    }

    /// <summary>Stops capture and shares a maximum two-second deadline across drain, flush and disposal.</summary>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_shutdown is not null) return _shutdown;
            _stopping = true;
            _deliveryCancellation.CancelAfter(TimeSpan.FromSeconds(2));
            return _shutdown = Task.Run(() => ShutdownCoreAsync(cancellationToken), CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(ShutdownAsync());

    internal IReadOnlyList<BootstrapPendingRecord> PendingRecords
    {
        get
        {
            lock (_gate)
                return _journal.Records.Concat((_logs?.PendingRecords ?? []).Select(record =>
                    new BootstrapPendingRecord(Guid.Parse(record.Attributes["diagnostics.record_id"].ToString()!),
                        _diagnosticScope, BootstrapTelemetryDestination.Log, record.Timestamp, null, record))).ToArray();
        }
    }

    private void CaptureDiagnostic(RemoteDiagnosticRecord diagnostic)
    {
        lock (_gate)
        {
            if (!_diagnosticsEnabled || _stopping) return;
            if (diagnostic.ShouldTrackException && diagnostic.Exception is not null && diagnostic.Level >= LogEventLevel.Error)
                AddExceptionDiagnostic(diagnostic);
            Signal();
        }
    }

    private void AddExceptionDiagnostic(RemoteDiagnosticRecord diagnostic)
    {
        Guid id = Guid.NewGuid();
        var attributes = new Dictionary<string, object>(diagnostic.Attributes, StringComparer.Ordinal)
        {
            ["diagnostics.record_id"] = id.ToString()
        };
        _journal.Add(new BootstrapPendingRecord(id, _diagnosticScope, BootstrapTelemetryDestination.Exception, diagnostic.Timestamp,
            null, diagnostic with { Attributes = attributes }));
    }

    private void CaptureLog(RemoteDiagnosticRecord diagnostic)
    {
        lock (_gate)
        {
            if (!_diagnosticsEnabled || _stopping) return;
            _logs?.Emit(diagnostic);
        }
    }

    private void PurgeDisallowed() => _journal.Purge(record => !Allowed(record));

    private void TransferLegacyLogs()
    {
        foreach (BootstrapPendingRecord record in _journal.Records.Where(record =>
            record.Destination == BootstrapTelemetryDestination.Log).ToArray())
        {
            if (record.State == BootstrapDeliveryState.Acknowledged)
            {
                _journal.Purge(candidate => candidate.Id == record.Id);
                continue;
            }
            RemoteDiagnosticRecord log = record.Diagnostic! with
            {
                Attributes = new Dictionary<string, object>(record.Diagnostic!.Attributes, StringComparer.Ordinal)
                {
                    ["diagnostics.record_id"] = record.Id.ToString("N")
                },
                ShouldTrackException = false
            };
            if (_logs?.TryImport(log) == true)
                _journal.Purge(candidate => candidate.Id == record.Id);
        }
    }

    private bool Allowed(BootstrapPendingRecord record) => record.Destination == BootstrapTelemetryDestination.Analytics
        ? _usageEnabled && record.Scope == _usageScope
        : _diagnosticsEnabled && record.Scope == _diagnosticScope;

    private void Expire()
    {
        if (_clockUsable) _journal.Purge(record => _previousBootIds.Contains(record.Id) && _time.GetUtcNow() - record.Timestamp > TimeSpan.FromDays(7));
    }

    private void StartWorker()
    {
        if (_worker is not null) return;
        _worker = Task.Run(DeliverAsync);
    }

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration registration = cancellationToken.Register(_deliveryCancellation.Cancel);
        lock (_gate)
        {
            StartWorker();
            Signal();
        }
        try
        {
            await Task.WhenAll(_worker!, _capture.FlushAsync(_deliveryCancellation.Token),
                    _logs?.FlushAsync(_deliveryCancellation.Token) ?? Task.CompletedTask)
                .WaitAsync(_deliveryCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_logs is not null) await _logs.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Signal()
    {
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    private async Task DeliverAsync()
    {
        IBootstrapTelemetryTransport? transport = null;
        try
        {
            while (true)
            {
                BootstrapPendingRecord? record;
                bool stopping;
                bool replay;
                lock (_gate)
                {
                    record = NextRecord();
                    stopping = _stopping;
                    replay = record is not null && _replayIds.Contains(record.Id);
                }
                if (record is null)
                {
                    if (stopping) break;
                    await _signal.WaitAsync(_deliveryCancellation.Token).ConfigureAwait(false);
                    continue;
                }
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(_deliveryCancellation.Token);
                if (replay)
                {
                    TimeSpan remaining = TimeSpan.FromSeconds(5) - _replayElapsed;
                    if (remaining <= TimeSpan.Zero) continue;
                    budget.CancelAfter(remaining);
                }
                else budget.CancelAfter(TimeSpan.FromSeconds(5));
                long started = _time.GetTimestamp();
                try
                {
                    Task<bool> delivery;
                    lock (_gate)
                    {
                        if (!Allowed(record)) continue;
                        transport ??= _transportFactory();
                        // Persist the attempt before enqueue: a crash cannot reset the replay cap.
                        record = record with { Attempts = Math.Min(record.Attempts, int.MaxValue - 1) + 1, State = BootstrapDeliveryState.HandedToTransport };
                        if (!_journal.Update(record)) continue;
                        delivery = transport.DeliverAsync(record, budget.Token);
                    }
                    bool acknowledged = await delivery.WaitAsync(budget.Token).ConfigureAwait(false);
                    if (acknowledged)
                    {
                        lock (_gate) _journal.Update(record with { State = BootstrapDeliveryState.Acknowledged });
                    }
                }
#pragma warning disable CA1031 // Telemetry failures cannot change the boot outcome.
                catch (Exception) { }
#pragma warning restore CA1031
                finally
                {
                    if (replay) _replayElapsed += _time.GetElapsedTime(started);
                }
                _deliveryCancellation.Token.ThrowIfCancellationRequested();
            }
            if (transport is not null)
                await transport.FlushAsync(_deliveryCancellation.Token).WaitAsync(_deliveryCancellation.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Network and exporter failures are best effort.
        catch (Exception) { }
#pragma warning restore CA1031
        finally
        {
            if (transport is not null)
            {
                try
                {
                    await transport.DisposeAsync().AsTask().WaitAsync(_deliveryCancellation.Token).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Disposal shares the terminal budget.
                catch (Exception) { }
#pragma warning restore CA1031
            }
        }
    }

    private BootstrapPendingRecord? NextRecord()
    {
        foreach (BootstrapPendingRecord record in _journal.Records)
        {
            if (record.Destination == BootstrapTelemetryDestination.Log || record.State == BootstrapDeliveryState.Acknowledged || !Allowed(record) || (record.Destination != BootstrapTelemetryDestination.Analytics && record.Attempts >= 3) || _attempted.Contains(record.Id)) continue;
            if (_replayIds.Contains(record.Id))
            {
                if (_replayed >= 100 || _replayElapsed >= TimeSpan.FromSeconds(5)) continue;
                _replayed++;
            }
            _attempted.Add(record.Id);
            return record;
        }
        return null;
    }

    private sealed class CaptureOnlyExporter : IRemoteDiagnosticsExporter
    {
        public ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
