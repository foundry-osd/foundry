// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Foundry.Telemetry;

/// <summary>
/// Sends sanitized Foundry telemetry to the PostHog capture API.
/// </summary>
public sealed class PostHogTelemetryService : ITelemetryService, IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object gate = new();
    private readonly Func<TelemetryConsentGeneration, HttpClient> transportFactory;
    private readonly TelemetryOptions options;
    private readonly TelemetryContext context;
    private readonly ILogger<PostHogTelemetryService>? logger;
    private readonly System.Threading.Channels.Channel<QueuedEvent> queue = System.Threading.Channels.Channel.CreateBounded<QueuedEvent>(
        new System.Threading.Channels.BoundedChannelOptions(64)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            SingleReader = false, // Disable can discard queued data; only the worker sends it.
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly List<Task> retirements = [];
    private readonly Task worker;
    private Transport? current;
    private TaskCompletionSource drained = CompletedSource();
    private Task? disposal;
    private int pending;
    private bool disposed;
    private long? shutdownStarted;
    private bool shutdownBudgetExhausted;

    /// <summary>Creates a locally queued telemetry service. Each factory result is owned and must use a fresh consent-bound handler.</summary>
    public PostHogTelemetryService(Func<TelemetryConsentGeneration, HttpClient> transportFactory,
        TelemetryOptions options, TelemetryContext context, ILogger<PostHogTelemetryService>? logger = null)
    {
        this.transportFactory = transportFactory;
        this.options = options;
        this.context = context;
        this.logger = logger;
        SetEnabled(options.IsEnabled);
        worker = Task.Run(ProcessQueueAsync);
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        lock (gate)
        {
            if (disposed) return;
            if (!enabled)
            {
                StopCurrent();
                return;
            }
            if (current is not null || !IsConfigured()) return;
            retirements.RemoveAll(task => task.IsCompleted);
            if (retirements.Count >= 8) return;
            var generation = new TelemetryConsentGeneration();
            try { current = new(generation, transportFactory(generation)); }
            catch (Exception error)
            {
                generation.Dispose();
                logger?.LogDebug(error, "Telemetry transport could not be initialized.");
            }
        }
    }

    /// <inheritdoc />
    public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested || !TelemetryEventPropertyPolicy.IsKnownEvent(eventName)) return Task.CompletedTask;
        try
        {
            lock (gate)
            {
                if (disposed || current is null) return Task.CompletedTask;
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new PostHogCapturePayload(options.ProjectToken, eventName,
                    options.InstallId, BuildProperties(eventName, properties)), JsonOptions);
                if (queue.Writer.TryWrite(new(current, bytes)))
                {
                    if (pending++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                else logger?.LogDebug("Telemetry queue is full; optional event dropped.");
            }
        }
        catch (Exception error) { logger?.LogDebug(error, "Telemetry capture was dropped."); }
        return Task.CompletedTask;
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                QueuedEvent? item;
                lock (gate)
                {
                    if (!queue.Reader.TryRead(out item)) continue;
                    if (item.Transport != current)
                    {
                        CompleteItem();
                        continue;
                    }
                }
                try
                {
                    using var content = new ByteArrayContent(item.Payload);
                    content.Headers.ContentType = new("application/json");
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    using HttpResponseMessage response = await item.Transport.Client.PostAsync(BuildCaptureEndpoint(options.HostUrl),
                        content, deadline.Token).ConfigureAwait(false);
                }
                catch (Exception error) { logger?.LogDebug(error, "Optional telemetry transport failed."); }
                finally { lock (gate) CompleteItem(); }
            }
        }
        catch (Exception error) { logger?.LogDebug(error, "Optional telemetry worker stopped."); }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task completion;
        lock (gate)
        {
            budget.CancelAfter(RemainingShutdownBudget());
            completion = drained.Task;
        }
        try { await completion.WaitAsync(budget.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            lock (gate)
            {
                shutdownBudgetExhausted = true;
                StopCurrent();
            }
        }
    }

    /// <summary>Stops capture and bounds waiting for the worker and observed transport retirement to two seconds.</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) return new(disposal);
            disposed = true;
            StopCurrent();
            queue.Writer.TryComplete();
            disposal = WaitForShutdownAsync(Task.WhenAll(retirements.Append(worker)), RemainingShutdownBudget());
            return new(disposal);
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private TimeSpan RemainingShutdownBudget()
    {
        shutdownStarted ??= System.Diagnostics.Stopwatch.GetTimestamp();
        if (shutdownBudgetExhausted) return TimeSpan.Zero;
        TimeSpan remaining = TimeSpan.FromSeconds(2) - System.Diagnostics.Stopwatch.GetElapsedTime(shutdownStarted.Value);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static async Task WaitForShutdownAsync(Task completion, TimeSpan remaining)
    {
        try { await completion.WaitAsync(remaining).ConfigureAwait(false); }
        catch (Exception) { }
    }

    private void StopCurrent()
    {
        Transport? previous = current;
        current = null;
        while (queue.Reader.TryRead(out _)) CompleteItem();
        if (previous is null) return;
        previous.Generation.Revoke();
        retirements.RemoveAll(task => task.IsCompleted);
        retirements.Add(Task.Run(async () =>
        {
            try { await previous.Generation.WaitForDrainAsync().ConfigureAwait(false); }
            finally
            {
                try { previous.Client.Dispose(); }
                catch (Exception) { }
                previous.Generation.Dispose();
            }
        }));
    }

    private void CompleteItem()
    {
        if (--pending == 0) drained.TrySetResult();
    }

    private bool IsConfigured() => !string.IsNullOrWhiteSpace(options.ProjectToken) && !string.IsNullOrWhiteSpace(options.InstallId) &&
        Uri.TryCreate(options.HostUrl, UriKind.Absolute, out Uri? host) && host.Scheme == Uri.UriSchemeHttps &&
        host.UserInfo.Length == 0 && host.Fragment.Length == 0 && host.Query.Length == 0;

    private static TaskCompletionSource CompletedSource()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult();
        return result;
    }

    private sealed record Transport(TelemetryConsentGeneration Generation, HttpClient Client);
    private sealed record QueuedEvent(Transport Transport, byte[] Payload);

    private static Uri BuildCaptureEndpoint(string hostUrl)
    {
        var baseUri = new Uri(hostUrl.EndsWith("/", StringComparison.Ordinal) ? hostUrl : hostUrl + "/");
        return new Uri(baseUri, "i/v0/e/");
    }

    private Dictionary<string, object> BuildProperties(string eventName, IReadOnlyDictionary<string, object?> properties)
    {
        Dictionary<string, object> finalProperties = new(StringComparer.Ordinal)
        {
            ["telemetry_schema_version"] = TelemetryDefaults.SchemaVersion,
            ["app"] = context.App,
            ["app_version"] = context.AppVersion,
            ["build_configuration"] = context.BuildConfiguration,
            ["app_runtime"] = context.Runtime,
            ["app_runtime_architecture"] = context.RuntimeArchitecture,
            ["app_locale"] = context.Locale,
            ["session_id"] = context.SessionId,
            ["$process_person_profile"] = false,
            ["$geoip_disable"] = false
        };

        foreach ((string key, object? value) in TelemetryEventPropertyPolicy.Sanitize(eventName, properties))
        {
            if (value is not null)
            {
                finalProperties[key] = value;
            }
        }

        AddEventContext(eventName, finalProperties);

        return finalProperties;
    }

    private void AddEventContext(string eventName, IDictionary<string, object> finalProperties)
    {
        if (eventName == TelemetryEvents.ConnectSessionReady)
        {
            finalProperties["boot_media_target"] = context.BootMediaTarget;
            finalProperties["connect_runtime_payload_source"] = context.RuntimePayloadSource;
        }
        else if (eventName == TelemetryEvents.DeploySessionFinished)
        {
            finalProperties["boot_media_target"] = context.BootMediaTarget;
            finalProperties["deploy_runtime_payload_source"] = context.RuntimePayloadSource;
        }
    }

    private sealed record PostHogCapturePayload(
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("distinct_id")] string DistinctId,
        [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, object> Properties);
}
