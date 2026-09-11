// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Protobuf;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.OpenTelemetry;

namespace Foundry.Telemetry;

/// <summary>
/// Distinguishes terminal acknowledgments from requests that must remain in durable storage.
/// </summary>
internal enum LogBatchDisposition
{
    Accepted,
    Retry,
    Rejected,
    /// <summary>
    /// No records were accepted; retry fewer records to fit the collector's request size limit.
    /// </summary>
    Split
}

/// <summary>
/// A rejected result retires the entire batch: OTLP does not identify individual rejected records.
/// </summary>
internal sealed record LogBatchResult(
    LogBatchDisposition Disposition,
    long RejectedRecordCount = 0,
    TimeSpan? RetryAfter = null);

/// <summary>
/// Sends an already persisted batch and reports the collector's response, without hidden retries or queues.
/// </summary>
internal interface ILogBatchTransport : IDisposable
{
    /// <summary>
    /// Returns only after the server responds; cancellation never acknowledges the batch.
    /// </summary>
    Task<LogBatchResult> SendAsync(IReadOnlyList<RemoteDiagnosticRecord> records, CancellationToken token);
}

/// <summary>
/// Uses Serilog's OTLP serializer with an acknowledged HTTP transport controlled by the durable queue.
/// </summary>
internal sealed class OtlpLogTransport : ILogBatchTransport
{
    private static readonly string[] ResourceAttributeNames =
    [
        "service.name", "service.version", "service.release", "runtime.name", "runtime.architecture"
    ];

    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly RemoteDiagnosticsContext _context;

    /// <summary>
    /// Creates a bounded HTTP client; redirects are disabled to keep credentials on the configured host.
    /// </summary>
    internal OtlpLogTransport(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        if (!options.CanSend)
        {
            throw new ArgumentException("Enabled diagnostics and a valid HTTPS endpoint are required.", nameof(options));
        }
        _endpoint = new Uri(options.HostUrl.TrimEnd('/') + "/i/v1/logs", UriKind.Absolute);
        _context = context;
        _client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 64 * 1024
        };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ProjectToken);
    }

    /// <inheritdoc />
    public async Task<LogBatchResult> SendAsync(IReadOnlyList<RemoteDiagnosticRecord> records, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(records);
        token.ThrowIfCancellationRequested();
        if (records.Count == 0)
        {
            return new LogBatchResult(LogBatchDisposition.Accepted);
        }

        byte[] payload = Serialize(records, token);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        try
        {
            using HttpResponseMessage response = await _client.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge && records.Count > 1)
            {
                return new LogBatchResult(LogBatchDisposition.Split);
            }
            if (response.StatusCode == HttpStatusCode.RequestTimeout ||
                response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                return new LogBatchResult(LogBatchDisposition.Retry, RetryAfter: GetRetryAfter(response));
            }
            if (!response.IsSuccessStatusCode)
            {
                return new LogBatchResult(LogBatchDisposition.Rejected, records.Count);
            }

            byte[] body = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            // PostHog currently returns application/json, even for protobuf requests. Other OTLP
            // collectors return protobuf; support both without mistaking malformed responses for receipts.
            long rejected = string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
                ? ReadJsonRejectedCount(body) : ReadRejectedCount(body);
            if (rejected < 0 || rejected > records.Count)
            {
                return new LogBatchResult(LogBatchDisposition.Retry);
            }
            return rejected == 0
                ? new LogBatchResult(LogBatchDisposition.Accepted)
                : new LogBatchResult(LogBatchDisposition.Rejected, rejected);
        }
        catch (HttpRequestException)
        {
            return new LogBatchResult(LogBatchDisposition.Retry);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new LogBatchResult(LogBatchDisposition.Retry);
        }
        catch (InvalidProtocolBufferException)
        {
            // A malformed response cannot prove acceptance. Stable event IDs identify possible replay duplicates.
            return new LogBatchResult(LogBatchDisposition.Retry);
        }
        catch (JsonException)
        {
            return new LogBatchResult(LogBatchDisposition.Retry);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private byte[] Serialize(IReadOnlyList<RemoteDiagnosticRecord> records, CancellationToken token)
    {
        using var batch = new MemoryStream();
        foreach (RemoteDiagnosticRecord record in records)
        {
            token.ThrowIfCancellationRequested();
            using var capture = new SerializationHandler(batch);
            using Serilog.Core.Logger serializer = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .AuditTo.OpenTelemetry(options =>
                {
                    options.LogsEndpoint = _endpoint.ToString();
                    options.Protocol = OtlpProtocol.HttpProtobuf;
                    options.HttpMessageHandler = capture;
                    options.ResourceAttributes = CreateResourceAttributes(record);
                    options.IncludedData = IncludedData.SpecRequiredResourceAttributes;
                })
                .CreateLogger();
            serializer.Write(CreateLogEvent(record));
        }
        // ExportLogsServiceRequest contains only repeated resource_logs (field 1). Protobuf concatenation
        // merges repeated fields, preserving the package's encoding and each record's original resource.
        return batch.ToArray();
    }

    private Dictionary<string, object> CreateResourceAttributes(RemoteDiagnosticRecord record)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["service.name"] = _context.App,
            ["service.version"] = _context.AppVersion,
            ["service.release"] = _context.Release,
            ["runtime.name"] = _context.Runtime,
            ["runtime.architecture"] = _context.RuntimeArchitecture
        };
        foreach (string name in ResourceAttributeNames)
        {
            if (record.Attributes.TryGetValue(name, out object? value))
            {
                attributes[name] = value is JsonElement json ? json.ToString() : value;
            }
        }
        return attributes;
    }

    private static LogEvent CreateLogEvent(RemoteDiagnosticRecord record)
    {
        var properties = record.Attributes
            .Where(attribute => !ResourceAttributeNames.Contains(attribute.Key, StringComparer.Ordinal))
            .Select(attribute => new LogEventProperty(attribute.Key, ConvertValue(attribute.Value)))
            .ToDictionary(property => property.Name, StringComparer.Ordinal);
        if (record.Exception is not null)
        {
            properties["exception.type"] = new LogEventProperty("exception.type", new ScalarValue(record.Exception.Type));
            properties["exception.message"] = new LogEventProperty("exception.message", new ScalarValue(record.Exception.Message));
            if (!string.IsNullOrEmpty(record.Exception.StackTrace))
            {
                properties["exception.stacktrace"] = new LogEventProperty("exception.stacktrace", new ScalarValue(record.Exception.StackTrace));
            }
            if (record.Exception.InnerExceptions.Count > 0)
            {
                properties["exception.inner_exceptions"] = new LogEventProperty("exception.inner_exceptions",
                    ConvertValue(JsonSerializer.SerializeToElement(record.Exception.InnerExceptions)));
            }
        }
        var template = new MessageTemplateParser().Parse(record.Body
            .Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
        return new LogEvent(record.Timestamp, record.Level, null, template, properties.Values);
    }

    private static LogEventPropertyValue ConvertValue(object? value) => value switch
    {
        JsonElement json => ConvertJson(json),
        LogEventPropertyValue property => property,
        IReadOnlyDictionary<string, object> dictionary => new DictionaryValue(dictionary.Select(pair =>
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(pair.Key), ConvertValue(pair.Value)))),
        IDictionary dictionary => new DictionaryValue(dictionary.Cast<DictionaryEntry>().Select(pair =>
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(pair.Key), ConvertValue(pair.Value)))),
        string => new ScalarValue(value),
        IEnumerable sequence => new SequenceValue(sequence.Cast<object?>().Select(ConvertValue)),
        _ => new ScalarValue(value)
    };

    private static LogEventPropertyValue ConvertJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => new DictionaryValue(value.EnumerateObject().Select(property =>
            new KeyValuePair<ScalarValue, LogEventPropertyValue>(new ScalarValue(property.Name), ConvertJson(property.Value)))),
        JsonValueKind.Array => new SequenceValue(value.EnumerateArray().Select(ConvertJson)),
        JsonValueKind.String => new ScalarValue(value.GetString()),
        JsonValueKind.Number => value.TryGetInt64(out long integer) ? new ScalarValue(integer) : new ScalarValue(value.GetDouble()),
        JsonValueKind.True => new ScalarValue(true),
        JsonValueKind.False => new ScalarValue(false),
        _ => new ScalarValue(null)
    };

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retry = response.Headers.RetryAfter;
        TimeSpan? delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return delay is { } value && value < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static long ReadRejectedCount(byte[] body)
    {
        using var input = new CodedInputStream(body);
        long rejected = 0;
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            if (tag != 10)
            {
                input.SkipLastField();
                continue;
            }
            // ExportLogsServiceResponse.partial_success (field 1), rejected_log_records (field 1).
            using var partial = new CodedInputStream(input.ReadBytes().ToByteArray());
            uint partialTag;
            while ((partialTag = partial.ReadTag()) != 0)
            {
                if (partialTag == 8)
                {
                    rejected = partial.ReadInt64();
                }
                else
                {
                    partial.SkipLastField();
                }
            }
        }
        return rejected;
    }

    private static long ReadJsonRejectedCount(byte[] body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _))
            throw new JsonException("The response is not an OTLP acknowledgment.");
        if (!root.TryGetProperty("partialSuccess", out JsonElement partial) &&
            !root.TryGetProperty("partial_success", out partial)) return 0;
        if (partial.ValueKind != JsonValueKind.Object)
            throw new JsonException("The response has an invalid partial-success object.");
        if (!partial.TryGetProperty("rejectedLogRecords", out JsonElement rejected) &&
            !partial.TryGetProperty("rejected_log_records", out rejected)) return 0;
        if (rejected.ValueKind == JsonValueKind.Number && rejected.TryGetInt64(out long count)) return count;
        if (rejected.ValueKind == JsonValueKind.String &&
            long.TryParse(rejected.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) return count;
        throw new JsonException("The response has an invalid rejected-log count.");
    }

    /// <summary>
    /// Captures the sink's supported synchronous audit serialization without performing network I/O.
    /// </summary>
    private sealed class SerializationHandler(Stream destination) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using (request)
            {
                request.Content!.CopyTo(destination, null, cancellationToken);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
