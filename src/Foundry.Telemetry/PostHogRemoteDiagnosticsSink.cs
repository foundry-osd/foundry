// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Foundry.Utilities.Diagnostics;
using Microsoft.Extensions.Options;
using PostHog;
using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>
/// Captures complete Logs independently from the conservative, rate-limited Error Tracking channel.
/// </summary>
public sealed class PostHogRemoteDiagnosticsSink : IRemoteDiagnosticsService, IDisposable
{
    private const int DefaultQueueCapacity = 256;
    private const int MaximumFingerprintEntries = 512;
    private const int MaximumEventsPerFingerprintWindow = 5;
    private static readonly TimeSpan FingerprintWindow = TimeSpan.FromMinutes(1);
    private readonly object _gate = new();
    private readonly Func<RemoteDiagnosticsOptions, RemoteDiagnosticsContext, IRemoteDiagnosticsExporter> _exporterFactory;
    private readonly int _queueCapacity;
    private readonly Action<RemoteDiagnosticRecord>? _capture;
    private readonly Action<RemoteDiagnosticRecord>? _logCapture;
    private readonly bool _productionLogs;
    private ReliableLogPipeline? _logs;
    private RemoteDiagnosticsOptions? _configuredOptions;
    private readonly List<Task> _retired = [];
    private readonly TimeProvider _timeProvider;
    private ConditionalWeakTable<Exception, ExceptionDedupeState> _seenExceptions = new();
    private readonly Dictionary<string, FingerprintWindowState> _fingerprints = new(StringComparer.Ordinal);
    private Channel<QueuedRemoteDiagnosticRecord>? _channel;
    private IRemoteDiagnosticsExporter? _exporter;
    private RemoteDiagnosticsContext? _context;
    private Task _worker = Task.CompletedTask;
    private int _accepting;
    private int _stopping;
    private int _disposed;
    private int _consentGeneration;
    private long _droppedRecordCount;

    /// <summary>
    /// Initializes a production PostHog diagnostics service.
    /// </summary>
    public PostHogRemoteDiagnosticsSink()
        : this(static (options, context) => new PostHogDiagnosticsExporter(options, context), DefaultQueueCapacity)
    {
        _productionLogs = true;
    }

    internal PostHogRemoteDiagnosticsSink(
        Func<RemoteDiagnosticsOptions, RemoteDiagnosticsContext, IRemoteDiagnosticsExporter> exporterFactory,
        int queueCapacity = DefaultQueueCapacity,
        TimeProvider? timeProvider = null,
        Action<RemoteDiagnosticRecord>? capture = null,
        Action<RemoteDiagnosticRecord>? logCapture = null)
    {
        ArgumentNullException.ThrowIfNull(exporterFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);
        _exporterFactory = exporterFactory;
        _capture = capture;
        _logCapture = logCapture;
        _queueCapacity = queueCapacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal long DroppedRecordCount => Interlocked.Read(ref _droppedRecordCount);

    /// <inheritdoc />
    public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        options = options with { LogDirectory = options.LogDirectory ?? RemoteDiagnosticsSink.LogDirectory };
        if (!options.CanSend || Volatile.Read(ref _stopping) != 0)
        {
            if (_productionLogs && _logs is null && !options.IsEnabled)
            {
                using var pending = new DurableLogQueue(RemoteLogStorage.Resolve(options, context));
                pending.Clear();
            }
            Disable();
            return;
        }

        lock (_gate)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                return;
            }

            if (_exporter is not null)
            {
                if (_configuredOptions == options)
                {
                    _context = context;
                    _logs?.Enable();
                    Volatile.Write(ref _accepting, 1);
                    return;
                }
                Disable();
                _channel?.Writer.TryComplete();
                _retired.Add(RetireAsync(_worker, _logs, _exporter));
                _logs = null;
                _exporter = null;
            }

            try
            {
                _exporter = _exporterFactory(options, context);
                _context = context;
                _configuredOptions = options;
                if (_productionLogs)
                    _logs = new ReliableLogPipeline(new OtlpLogTransport(options, context), RemoteLogStorage.Resolve(options, context));
                _channel = Channel.CreateBounded<QueuedRemoteDiagnosticRecord>(new BoundedChannelOptions(_queueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
                _worker = ProcessQueueAsync(_channel.Reader, _exporter);
                Volatile.Write(ref _accepting, 1);
            }
#pragma warning disable CA1031 // Diagnostics transport must never affect application startup.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Debug.WriteLine($"Remote diagnostics configuration failed: {ex.GetType().Name}");
                _exporter = null;
                _context = null;
                _channel = null;
                Volatile.Write(ref _accepting, 0);
            }
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        lock (_gate)
        {
            Volatile.Write(ref _accepting, 0);
            Interlocked.Increment(ref _consentGeneration);
            _fingerprints.Clear();
            _seenExceptions = new ConditionalWeakTable<Exception, ExceptionDedupeState>();
            _logs?.Disable();
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (Volatile.Read(ref _stopping) != 0 || HasTrueScalar(logEvent, "PostHogTransportInternal")) return;
        try
        {
            lock (_gate)
            {
                if (!HasTrueScalar(logEvent, "RemoteDiagnosticsInternal") &&
                    Volatile.Read(ref _accepting) != 0 && _context is not null && (_logs is not null || _logCapture is not null))
                {
                    RemoteDiagnosticRecord log = LogRecordFactory.Create(logEvent, _context);
                    _logs?.Emit(log);
                    _logCapture?.Invoke(log);
                }
            }
        }
#pragma warning disable CA1031 // Local storage/serialization failures must not suppress independent error tracking.
        catch (Exception ex) { Debug.WriteLine($"Log capture failed: {ex.GetType().Name}"); }
#pragma warning restore CA1031
        if (Volatile.Read(ref _stopping) != 0 || !ShouldExport(logEvent))
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                Channel<QueuedRemoteDiagnosticRecord>? channel = _channel;
                RemoteDiagnosticsContext? context = _context;
                if (Volatile.Read(ref _accepting) == 0 || channel is null || context is null)
                {
                    return;
                }

                if (!TryAcquireFingerprint(logEvent))
                {
                    Interlocked.Increment(ref _droppedRecordCount);
                    return;
                }

                RemoteDiagnosticRecord record = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(logEvent, context) with
                {
                    ShouldTrackException = logEvent.Exception is not null &&
                        logEvent.Level >= LogEventLevel.Error &&
                        !HasTrackedException(logEvent.Exception, GetScalarText(logEvent, "OperationId"))
                };
                // Cumulative local losses; SDK transport queues may drop additional records.
                long droppedRecordCount = Interlocked.Read(ref _droppedRecordCount);
                if (droppedRecordCount > 0)
                {
                    record = record with
                    {
                        Attributes = new Dictionary<string, object>(record.Attributes, StringComparer.Ordinal)
                        {
                            ["diagnostics.dropped_record_count"] = droppedRecordCount
                        }
                    };
                }

                var queuedRecord = new QueuedRemoteDiagnosticRecord(
                    Volatile.Read(ref _consentGeneration),
                    record);
                if (_capture is not null)
                {
                    _capture(record);
                    if (record.ShouldTrackException)
                    {
                        _seenExceptions.GetValue(logEvent.Exception!, static _ => new ExceptionDedupeState())
                            .OperationIds.Add(GetScalarText(logEvent, "OperationId"));
                    }
                    return;
                }

                if (!channel.Writer.TryWrite(queuedRecord))
                {
                    Interlocked.Increment(ref _droppedRecordCount);
                }
                else if (record.ShouldTrackException)
                {
                    _seenExceptions.GetValue(logEvent.Exception!, static _ => new ExceptionDedupeState())
                        .OperationIds.Add(GetScalarText(logEvent, "OperationId"));
                }
            }
        }
#pragma warning disable CA1031 // Logging must be fail-safe for every application call site.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Debug.WriteLine($"Remote diagnostics enqueue failed: {ex.GetType().Name}");
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Volatile.Write(ref _accepting, 0);
        }
        if (Interlocked.Exchange(ref _stopping, 1) == 0)
        {
            _channel?.Writer.TryComplete();
        }

        if (_logs is not null) await _logs.FlushAsync(cancellationToken).ConfigureAwait(false);

        await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_exporter is not null)
        {
            await _exporter.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await FlushAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        if (_exporter is not null)
        {
            try
            {
                await _exporter.DisposeAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Diagnostics transport disposal must not affect application shutdown.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Debug.WriteLine($"Remote diagnostics disposal failed: {ex.GetType().Name}");
            }
        }
        if (_logs is not null) await _logs.DisposeAsync().ConfigureAwait(false);
        await Task.WhenAll(_retired).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases transport resources for synchronous host disposal.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static async Task RetireAsync(Task worker, ReliableLogPipeline? logs, IRemoteDiagnosticsExporter exporter)
    {
        try
        {
            if (logs is not null) await logs.DisposeAsync().ConfigureAwait(false);
            await worker.ConfigureAwait(false);
            await exporter.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Superseded diagnostics cannot interrupt settings changes.
        catch (Exception ex) { Debug.WriteLine($"Retired diagnostics disposal failed: {ex.GetType().Name}"); }
#pragma warning restore CA1031
    }

    private static bool ShouldExport(LogEvent logEvent)
    {
        return logEvent.Exception is not null && logEvent.Level >= LogEventLevel.Error
            && !HasTrueScalar(logEvent, "RemoteDiagnosticsInternal")
            && !HasTrueScalar(logEvent, "PostHogTransportInternal");
    }

    private static bool HasTrueScalar(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out LogEventPropertyValue? propertyValue) &&
        propertyValue is ScalarValue { Value: true };

    private bool TryAcquireFingerprint(LogEvent logEvent)
    {
        string exceptionType = logEvent.Exception is LogExceptionSnapshot snapshot
            ? snapshot.OriginalType : logEvent.Exception?.GetType().FullName ?? string.Empty;
        string failureCode = GetScalarText(logEvent, "FailureCode");
        if (string.IsNullOrEmpty(failureCode))
        {
            failureCode = GetScalarText(logEvent, "ErrorCode");
        }

        string fingerprint = string.Join('|', logEvent.Level, logEvent.MessageTemplate.Text, exceptionType, failureCode,
            GetScalarText(logEvent, "OperationId"), GetScalarText(logEvent, "FailedOperationName"),
            GetScalarText(logEvent, "CurrentOperation"), GetScalarText(logEvent, "ProcessOperation"),
            GetScalarText(logEvent, "StepName"), GetScalarText(logEvent, "ToolName"));
        long now = _timeProvider.GetTimestamp();
        lock (_gate)
        {
            if (_fingerprints.Count >= MaximumFingerprintEntries && !_fingerprints.ContainsKey(fingerprint))
            {
                _fingerprints.Clear();
            }

            if (!_fingerprints.TryGetValue(fingerprint, out FingerprintWindowState? state) || _timeProvider.GetElapsedTime(state.StartedAt, now) >= FingerprintWindow)
            {
                _fingerprints[fingerprint] = new FingerprintWindowState(now, 1);
                return true;
            }

            if (state.Count >= MaximumEventsPerFingerprintWindow)
            {
                return false;
            }

            _fingerprints[fingerprint] = state with { Count = state.Count + 1 };
            return true;
        }
    }

    private static string GetScalarText(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out LogEventPropertyValue? value) && value is ScalarValue scalar
            ? scalar.Value?.ToString() ?? string.Empty
            : string.Empty;

    private bool HasTrackedException(Exception exception, string operationId) =>
        _seenExceptions.TryGetValue(exception, out ExceptionDedupeState? state) &&
        state.OperationIds.Contains(operationId);

    private async Task ProcessQueueAsync(
        ChannelReader<QueuedRemoteDiagnosticRecord> reader,
        IRemoteDiagnosticsExporter exporter)
    {
        await foreach (QueuedRemoteDiagnosticRecord queuedRecord in reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (queuedRecord.ConsentGeneration != Volatile.Read(ref _consentGeneration))
            {
                Interlocked.Increment(ref _droppedRecordCount);
                continue;
            }

            try
            {
                await exporter.ExportAsync(queuedRecord.Record, CancellationToken.None).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // A failed export must not stop later records from draining.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Debug.WriteLine($"Remote diagnostics export failed: {ex.GetType().Name}");
            }
        }
    }

    private sealed record FingerprintWindowState(long StartedAt, int Count);

    private sealed record QueuedRemoteDiagnosticRecord(
        int ConsentGeneration,
        RemoteDiagnosticRecord Record);

    private sealed class ExceptionDedupeState
    {
        public HashSet<string> OperationIds { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>Exports the conservative Error Tracking contract; Logs use acknowledged OTLP delivery.</summary>
internal sealed class PostHogDiagnosticsExporter : IRemoteDiagnosticsExporter
{
    private readonly IPostHogEventClient _eventClient;
    private readonly PostHogExceptionTracker _exceptionTracker;
    private int _disposed;

    public PostHogDiagnosticsExporter(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
    {
        var client = new PostHogClient(Options.Create(new PostHogOptions
        {
            ProjectToken = options.ProjectToken,
            HostUrl = new Uri(options.HostUrl, UriKind.Absolute),
            IsServer = true,
            MaxQueueSize = 256,
            MaxBatchSize = 50,
            FlushAt = 20,
            FlushInterval = TimeSpan.FromSeconds(5)
        }));
        _eventClient = new PostHogEventClient(client);
        _exceptionTracker = new PostHogExceptionTracker(_eventClient, options.InstallId);
    }

    public ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExportException(record);
        return ValueTask.CompletedTask;
    }

    internal void ExportException(RemoteDiagnosticRecord record) => _exceptionTracker.Track(record);

    public Task FlushAsync(CancellationToken cancellationToken) => _eventClient.FlushAsync().WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            await _eventClient.DisposeAsync().ConfigureAwait(false);
    }
}
