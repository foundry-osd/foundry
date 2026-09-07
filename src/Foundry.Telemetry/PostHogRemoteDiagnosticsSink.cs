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
    private readonly Func<RemoteDiagnosticsOptions, RemoteDiagnosticsContext, TelemetryConsentGeneration, IRemoteDiagnosticsExporter> _exporterFactory;
    private readonly int _queueCapacity;
    private readonly TimeProvider _timeProvider;
    private ConditionalWeakTable<Exception, ExceptionDedupeState> _seenExceptions = new();
    private readonly Dictionary<string, FingerprintWindowState> _fingerprints = new(StringComparer.Ordinal);
    private readonly List<DiagnosticsSession> _retiring = [];
    private DiagnosticsSession? _current;
    private bool _stopping;
    private int _disposed;
    private long _droppedRecordCount;

    public PostHogRemoteDiagnosticsSink()
        : this(static (options, context, generation) => new PostHogDiagnosticsExporter(options, context, generation), DefaultQueueCapacity) { }

    internal PostHogRemoteDiagnosticsSink(Func<RemoteDiagnosticsOptions, RemoteDiagnosticsContext, IRemoteDiagnosticsExporter> exporterFactory,
        int queueCapacity = DefaultQueueCapacity, TimeProvider? timeProvider = null)
        : this((options, context, _) => exporterFactory(options, context), queueCapacity, timeProvider) { }

    internal PostHogRemoteDiagnosticsSink(Func<RemoteDiagnosticsOptions, RemoteDiagnosticsContext, TelemetryConsentGeneration, IRemoteDiagnosticsExporter> exporterFactory,
        int queueCapacity = DefaultQueueCapacity, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(exporterFactory);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);
        _exporterFactory = exporterFactory;
        _queueCapacity = queueCapacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal long DroppedRecordCount => Interlocked.Read(ref _droppedRecordCount);

    public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        if (!options.CanSend) { Disable(); return; }
        lock (_gate)
        {
            if (_stopping || _disposed != 0 || _current is not null) return;
            _retiring.RemoveAll(session => session.Retirement.IsCompleted);
            if (_retiring.Count >= 8) return;
            var generation = new TelemetryConsentGeneration();
            try
            {
                var session = new DiagnosticsSession(generation, _exporterFactory(options, context, generation), context, _queueCapacity);
                _current = session;
                session.Worker = Task.Run(() => ProcessQueueAsync(session));
            }
            catch (Exception error)
            {
                generation.Dispose();
                Debug.WriteLine($"Remote diagnostics configuration failed: {error.GetType().Name}");
            }
        }
    }

    public void Disable()
    {
        lock (_gate)
        {
            if (_current is { } session)
            {
                _current = null;
                Retire(session);
            }
            _fingerprints.Clear();
            _seenExceptions = new ConditionalWeakTable<Exception, ExceptionDedupeState>();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        if (!ShouldExport(logEvent)) return;
        try
        {
            lock (_gate)
            {
                if (_stopping || _current is not { } session) return;
                if (logEvent.Exception is not null && !TryAcquireException(logEvent.Exception, GetScalarText(logEvent, "OperationId"))) return;
                if (!TryAcquireFingerprint(logEvent)) { Interlocked.Increment(ref _droppedRecordCount); return; }
                RemoteDiagnosticRecord record = RemoteDiagnosticPropertyPolicy.CreateSanitizedRecord(logEvent, session.Context);
                if (!session.Channel.Writer.TryWrite(record)) Interlocked.Increment(ref _droppedRecordCount);
            }
        }
        catch (Exception error) { Debug.WriteLine($"Remote diagnostics enqueue failed: {error.GetType().Name}"); }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        DiagnosticsSession? session;
        lock (_gate)
        {
            _stopping = true;
            session = _current;
            if (session is not null)
            {
                session.Channel.Writer.TryComplete();
                session.Flush ??= Task.Run(async () =>
                {
                    await session.Worker.ConfigureAwait(false);
                    await session.Exporter.FlushAsync(cancellationToken).ConfigureAwait(false);
                });
            }
        }
        try
        {
            if (session is not null)
            {
                await session.Flush!.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, session)) _current = null;
                if (session is not null) Retire(session);
            }
        }
        Task[] retiring;
        lock (_gate) retiring = _retiring.Select(item => item.Retirement).ToArray();
        await Task.WhenAll(retiring).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await FlushAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception error) { Debug.WriteLine($"Remote diagnostics shutdown abandoned: {error.GetType().Name}"); }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void Retire(DiagnosticsSession session)
    {
        if (session.IsRetiring) return;
        session.IsRetiring = true;
        session.Generation.Revoke();
        session.Channel.Writer.TryComplete();
        Task cancellation = session.Stop.CancelAsync();
        session.Retirement = Task.Run(async () =>
        {
            try
            {
                try { await cancellation.ConfigureAwait(false); }
                catch (Exception error) { Debug.WriteLine($"Diagnostics cancellation callback failed: {error.GetType().Name}"); }
                try
                {
                    await session.Worker.ConfigureAwait(false);
                    if (session.Flush is not null) await session.Flush.ConfigureAwait(false);
                }
                catch (Exception error) { Debug.WriteLine($"Diagnostics flush ended: {error.GetType().Name}"); }
                await session.Generation.WaitForDrainAsync().ConfigureAwait(false);
                await session.Exporter.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) { Debug.WriteLine($"Remote diagnostics retirement failed: {error.GetType().Name}"); }
            finally
            {
                session.Generation.Dispose();
                session.Stop.Dispose();
            }
        });
        _retiring.Add(session);
    }
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

    private async Task ProcessQueueAsync(DiagnosticsSession session)
    {
        try
        {
            await foreach (RemoteDiagnosticRecord record in session.Channel.Reader.ReadAllAsync(session.Stop.Token).ConfigureAwait(false))
            {
                session.Stop.Token.ThrowIfCancellationRequested();
                try { await session.Exporter.ExportAsync(record, session.Stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (session.Stop.IsCancellationRequested) { break; }
                catch (Exception error) { Debug.WriteLine($"Remote diagnostics export failed: {error.GetType().Name}"); }
            }
        }
        catch (OperationCanceledException) when (session.Stop.IsCancellationRequested) { }
        finally
        {
            while (session.Channel.Reader.TryRead(out _)) Interlocked.Increment(ref _droppedRecordCount);
        }
    }

    private sealed class DiagnosticsSession(TelemetryConsentGeneration generation, IRemoteDiagnosticsExporter exporter,
        RemoteDiagnosticsContext context, int capacity)
    {
        public TelemetryConsentGeneration Generation { get; } = generation;
        public IRemoteDiagnosticsExporter Exporter { get; } = exporter;
        public RemoteDiagnosticsContext Context { get; } = context;
        public CancellationTokenSource Stop { get; } = new();
        public Channel<RemoteDiagnosticRecord> Channel { get; } = System.Threading.Channels.Channel.CreateBounded<RemoteDiagnosticRecord>(new BoundedChannelOptions(capacity)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, AllowSynchronousContinuations = false });
        public Task Worker { get; set; } = Task.CompletedTask;
        public Task? Flush { get; set; }
        public Task Retirement { get; set; } = Task.CompletedTask;
        public bool IsRetiring { get; set; }
    }

    private sealed record FingerprintWindowState(DateTimeOffset StartedAt, int Count);
    private sealed class ExceptionDedupeState
    {
        public HashSet<string> OperationIds { get; } = new(StringComparer.Ordinal);
    }
}

internal sealed class PostHogDiagnosticsExporter : IRemoteDiagnosticsExporter
{
    private static readonly HashSet<string> ResourceAttributeNames = new(StringComparer.Ordinal)
    {
        "service.name",
        "service.version",
        "service.release",
        "runtime.name",
        "runtime.architecture"
    };

    private readonly Serilog.ILogger _logExporter;
    private readonly IPostHogEventClient _eventClient;
    private readonly PostHogExceptionTracker _exceptionTracker;
    private readonly object _shutdownGate = new();
    private Task? _logShutdown;
    private Task? _eventFlush;
    private int _disposed;

    public PostHogDiagnosticsExporter(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context,
        TelemetryConsentGeneration generation, Func<string, HttpMessageHandler>? handlerFactory = null)
    {
        handlerFactory ??= static _ => new SocketsHttpHandler { AllowAutoRedirect = false, ActivityHeadersPropagator = null };
        string logsEndpoint = options.HostUrl.TrimEnd('/') + "/i/v1/logs";
        _logExporter = new LoggerConfiguration()
            .WriteTo.OpenTelemetry(configuration =>
            {
                configuration.LogsEndpoint = logsEndpoint;
                configuration.Protocol = OtlpProtocol.HttpProtobuf;
                configuration.HttpMessageHandler = new ConsentHttpMessageHandler(generation, handlerFactory("logs"));
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
        }), httpClientFactory: new DiagnosticsHttpClientFactory(generation, handlerFactory));
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
        var properties = new List<LogEventProperty>(record.Attributes.Count + 3);
        properties.AddRange(record.Attributes
            .Where(static attribute => !ResourceAttributeNames.Contains(attribute.Key))
            .Select(static attribute =>
                new LogEventProperty(attribute.Key, new ScalarValue(attribute.Value))));
        if (record.Exception is not null)
        {
            properties.Add(new LogEventProperty("exception.type", new ScalarValue(record.Exception.Type)));
            if (!string.Equals(record.Exception.Message, record.Body, StringComparison.Ordinal))
            {
                properties.Add(new LogEventProperty("exception.message", new ScalarValue(record.Exception.Message)));
            }

            if (!string.IsNullOrWhiteSpace(record.Exception.StackTrace))
            {
                properties.Add(new LogEventProperty("exception.stacktrace", new ScalarValue(record.Exception.StackTrace)));
            }
        }

        return new LogEvent(
            record.Timestamp,
            record.Level,
            exception: null,
            CreateLiteralMessageTemplate(record.Body),
            properties);
    }

    private static MessageTemplate CreateLiteralMessageTemplate(string message) =>
        new MessageTemplateParser().Parse(
            message
                .Replace("{", "{{", StringComparison.Ordinal)
                .Replace("}", "}}", StringComparison.Ordinal));

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await GetLogShutdown().WaitAsync(cancellationToken).ConfigureAwait(false);
        Task eventFlush;
        lock (_shutdownGate) eventFlush = _eventFlush ??= _eventClient.FlushAsync();
        await eventFlush.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await GetLogShutdown().ConfigureAwait(false);
            Task? flush;
            lock (_shutdownGate) flush = _eventFlush;
            if (flush is not null) await flush.ConfigureAwait(false);
        }
        finally { await _eventClient.DisposeAsync().ConfigureAwait(false); }
    }

    private Task GetLogShutdown()
    {
        lock (_shutdownGate)
        {
            return _logShutdown ??= Task.Run(() => (_logExporter as IDisposable)?.Dispose());
        }
    }

    private sealed class DiagnosticsHttpClientFactory(TelemetryConsentGeneration generation, Func<string, HttpMessageHandler> handlerFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ConsentHttpMessageHandler(generation, handlerFactory("exceptions")))
        { Timeout = Timeout.InfiniteTimeSpan };
    }
}
