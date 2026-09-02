// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PostHog;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.OpenTelemetry;

namespace Foundry.Telemetry;

/// <summary>
/// Sanitizes and queues eligible log events for best-effort PostHog delivery.
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
    }

    internal PostHogRemoteDiagnosticsSink(
        Func<RemoteDiagnosticsOptions, RemoteDiagnosticsContext, IRemoteDiagnosticsExporter> exporterFactory,
        int queueCapacity = DefaultQueueCapacity,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(exporterFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);
        _exporterFactory = exporterFactory;
        _queueCapacity = queueCapacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal long DroppedRecordCount => Interlocked.Read(ref _droppedRecordCount);

    /// <inheritdoc />
    public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        if (!options.CanSend || Volatile.Read(ref _stopping) != 0)
        {
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
                Volatile.Write(ref _accepting, 1);
                return;
            }

            try
            {
                _exporter = _exporterFactory(options, context);
                _context = context;
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
        }
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
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

                if (logEvent.Exception is not null && !TryAcquireException(logEvent.Exception, GetScalarText(logEvent, "OperationId")))
                {
                    return;
                }

                if (!TryAcquireFingerprint(logEvent))
                {
                    Interlocked.Increment(ref _droppedRecordCount);
                    return;
                }

                RemoteDiagnosticRecord record = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(logEvent, context);
                var queuedRecord = new QueuedRemoteDiagnosticRecord(
                    Volatile.Read(ref _consentGeneration),
                    record);
                if (!channel.Writer.TryWrite(queuedRecord))
                {
                    Interlocked.Increment(ref _droppedRecordCount);
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
    }

    /// <summary>
    /// Releases transport resources for synchronous host disposal.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static bool ShouldExport(LogEvent logEvent)
    {
        if (HasTrueScalar(logEvent, "RemoteDiagnosticsInternal"))
        {
            return false;
        }

        return logEvent.Level switch
        {
            LogEventLevel.Fatal or LogEventLevel.Error or LogEventLevel.Warning => true,
            LogEventLevel.Information => HasTrueScalar(logEvent, "RemoteDiagnostic"),
            _ => false
        };
    }

    private static bool HasTrueScalar(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out LogEventPropertyValue? propertyValue) &&
        propertyValue is ScalarValue { Value: true };

    private bool TryAcquireFingerprint(LogEvent logEvent)
    {
        string exceptionType = logEvent.Exception?.GetType().FullName ?? string.Empty;
        string failureCode = GetScalarText(logEvent, "FailureCode");
        if (string.IsNullOrEmpty(failureCode))
        {
            failureCode = GetScalarText(logEvent, "ErrorCode");
        }

        string fingerprint = string.Join('|', logEvent.Level, logEvent.MessageTemplate.Text, exceptionType, failureCode);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_fingerprints.Count >= MaximumFingerprintEntries && !_fingerprints.ContainsKey(fingerprint))
            {
                _fingerprints.Clear();
            }

            if (!_fingerprints.TryGetValue(fingerprint, out FingerprintWindowState? state) || now - state.StartedAt >= FingerprintWindow)
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

    private bool TryAcquireException(Exception exception, string operationId)
    {
        ExceptionDedupeState state = _seenExceptions.GetValue(exception, static _ => new ExceptionDedupeState());
        lock (state.OperationIds)
        {
            return state.OperationIds.Add(operationId);
        }
    }

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

    private sealed record FingerprintWindowState(DateTimeOffset StartedAt, int Count);

    private sealed record QueuedRemoteDiagnosticRecord(
        int ConsentGeneration,
        RemoteDiagnosticRecord Record);

    private sealed class ExceptionDedupeState
    {
        public HashSet<string> OperationIds { get; } = new(StringComparer.Ordinal);
    }
}

internal sealed class PostHogDiagnosticsExporter : IRemoteDiagnosticsExporter
{
    private static readonly MessageTemplate DiagnosticMessageTemplate =
        new MessageTemplateParser().Parse("{DiagnosticBody:l}");

    private readonly Serilog.ILogger _logExporter;
    private readonly IPostHogEventClient _eventClient;
    private readonly PostHogExceptionTracker _exceptionTracker;
    private int _logExporterDisposed;
    private int _disposed;

    public PostHogDiagnosticsExporter(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
    {
        string logsEndpoint = options.HostUrl.TrimEnd('/') + "/i/v1/logs";
        _logExporter = new LoggerConfiguration()
            .WriteTo.OpenTelemetry(configuration =>
            {
                configuration.LogsEndpoint = logsEndpoint;
                configuration.Protocol = OtlpProtocol.HttpProtobuf;
                configuration.Headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Authorization"] = $"Bearer {options.ProjectToken}"
                };
                configuration.ResourceAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["service.name"] = RemoteDiagnosticPropertyPolicy.SanitizeResourceValue(context.App),
                    ["service.version"] = RemoteDiagnosticPropertyPolicy.SanitizeResourceValue(context.AppVersion),
                    ["service.release"] = RemoteDiagnosticPropertyPolicy.SanitizeResourceValue(context.Release),
                    ["runtime.name"] = RemoteDiagnosticPropertyPolicy.SanitizeResourceValue(context.Runtime),
                    ["runtime.architecture"] = RemoteDiagnosticPropertyPolicy.SanitizeResourceValue(context.RuntimeArchitecture)
                };
                configuration.IncludedData = IncludedData.SpecRequiredResourceAttributes;
                configuration.BatchingOptions.BatchSizeLimit = 50;
                configuration.BatchingOptions.QueueLimit = 256;
                configuration.BatchingOptions.BufferingTimeLimit = TimeSpan.FromSeconds(2);
            }, ignoreEnvironment: true)
            .CreateLogger();

        var postHogClient = new PostHogClient(Options.Create(new PostHogOptions
        {
            ProjectToken = options.ProjectToken,
            HostUrl = new Uri(options.HostUrl, UriKind.Absolute),
            IsServer = true,
            MaxQueueSize = 256,
            MaxBatchSize = 50,
            FlushAt = 20,
            FlushInterval = TimeSpan.FromSeconds(5)
        }));
        _eventClient = new PostHogEventClient(postHogClient);
        _exceptionTracker = new PostHogExceptionTracker(_eventClient, options.InstallId);
    }

    public ValueTask ExportAsync(RemoteDiagnosticRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logExporter.Write(CreateLogEvent(record));
        _exceptionTracker.Track(record);
        return ValueTask.CompletedTask;
    }

    internal static LogEvent CreateLogEvent(RemoteDiagnosticRecord record)
    {
        var properties = new List<LogEventProperty>(record.Attributes.Count + 4)
        {
            new("DiagnosticBody", new ScalarValue(record.Body))
        };
        properties.AddRange(record.Attributes.Select(static attribute =>
            new LogEventProperty(attribute.Key, new ScalarValue(attribute.Value))));
        if (record.Exception is not null)
        {
            properties.Add(new LogEventProperty("exception.type", new ScalarValue(record.Exception.Type)));
            properties.Add(new LogEventProperty("exception.message", new ScalarValue(record.Exception.Message)));
            if (!string.IsNullOrWhiteSpace(record.Exception.StackTrace))
            {
                properties.Add(new LogEventProperty("exception.stacktrace", new ScalarValue(record.Exception.StackTrace)));
            }
        }

        return new LogEvent(
            record.Timestamp,
            record.Level,
            exception: null,
            DiagnosticMessageTemplate,
            properties);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await Task.Run(DisposeLogExporter).WaitAsync(cancellationToken).ConfigureAwait(false);
        await _eventClient.FlushAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        DisposeLogExporter();
        await _eventClient.DisposeAsync().ConfigureAwait(false);
    }

    private void DisposeLogExporter()
    {
        if (Interlocked.Exchange(ref _logExporterDisposed, 1) == 0 && _logExporter is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
