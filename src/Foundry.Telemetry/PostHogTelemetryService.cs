using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Foundry.Telemetry;

/// <summary>
/// Sends sanitized Foundry telemetry to the PostHog capture API.
/// </summary>
public sealed class PostHogTelemetryService : ITelemetryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly TelemetryOptions options;
    private readonly TelemetryContext context;
    private readonly ILogger<PostHogTelemetryService>? logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostHogTelemetryService"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client used for PostHog ingestion.</param>
    /// <param name="options">Runtime telemetry options.</param>
    /// <param name="context">Common event context.</param>
    /// <param name="logger">Optional logger for developer diagnostics.</param>
    public PostHogTelemetryService(
        HttpClient httpClient,
        TelemetryOptions options,
        TelemetryContext context,
        ILogger<PostHogTelemetryService>? logger = null)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.context = context;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
    {
        if (!options.CanSend)
        {
            logger?.LogDebug("Telemetry event {EventName} skipped because telemetry is disabled or not configured.", eventName);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            logger?.LogDebug("Telemetry event {EventName} skipped because cancellation was requested.", eventName);
            return;
        }

        if (!TelemetryEventPropertyPolicy.IsKnownEvent(eventName))
        {
            logger?.LogDebug("Telemetry event {EventName} skipped because it is not part of the approved taxonomy.", eventName);
            return;
        }

        try
        {
            Dictionary<string, object> finalProperties = BuildProperties(eventName, properties);
            var payload = new PostHogCapturePayload(
                options.ProjectToken,
                eventName,
                options.InstallId,
                finalProperties);

            using HttpResponseMessage response = await httpClient
                .PostAsJsonAsync(BuildCaptureEndpoint(options.HostUrl), payload, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                logger?.LogDebug(
                    "Telemetry event {EventName} sent. App={App}, PropertyCount={PropertyCount}.",
                    eventName,
                    context.App,
                    finalProperties.Count);
            }
            else
            {
                logger?.LogDebug(
                    "Telemetry event {EventName} failed with HTTP status {StatusCode}.",
                    eventName,
                    (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogDebug(exception, "Failed to capture telemetry event {EventName}.", eventName);
        }
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!options.CanSend)
        {
            logger?.LogDebug("Telemetry flush skipped because telemetry is disabled or not configured.");
            return Task.CompletedTask;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            logger?.LogDebug("Telemetry flush skipped because cancellation was requested.");
            return Task.CompletedTask;
        }

        logger?.LogDebug("Telemetry flush skipped because events are sent immediately.");
        return Task.CompletedTask;
    }

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
            ["runtime"] = context.Runtime,
            ["runtime_payload_source"] = context.RuntimePayloadSource,
            ["boot_media_target"] = context.BootMediaTarget,
            ["runtime_architecture"] = context.RuntimeArchitecture,
            ["locale"] = context.Locale,
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

        return finalProperties;
    }

    private sealed record PostHogCapturePayload(
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("event")] string Event,
        [property: JsonPropertyName("distinct_id")] string DistinctId,
        [property: JsonPropertyName("properties")] IReadOnlyDictionary<string, object> Properties);
}
