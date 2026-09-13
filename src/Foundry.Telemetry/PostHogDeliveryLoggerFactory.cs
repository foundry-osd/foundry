// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using PostHog.Library;
using Serilog.Events;
using Serilog.Parsing;

namespace Foundry.Telemetry;

/// <summary>Contains only fixed delivery reasons and numeric HTTP status, never SDK messages or payloads.</summary>
internal sealed record ExceptionDeliveryFailure(string Reason, int? HttpStatusCode = null)
{
    internal LogEvent CreateLogEvent() => new(DateTimeOffset.UtcNow, LogEventLevel.Warning, null,
        new MessageTemplateParser().Parse("PostHog Error Tracking delivery failed. Reason={FailureReason}, HttpStatusCode={HttpStatusCode}"),
        [new("FailureReason", new ScalarValue(Reason)), new("HttpStatusCode", new ScalarValue(HttpStatusCode)),
            new("SourceContext", new ScalarValue("Foundry.Telemetry.ExceptionDelivery")),
            new("PostHogTransportInternal", new ScalarValue(true))]);
}

/// <summary>
/// Observes the pinned SDK's batch failure and queue-loss events without formatting untrusted SDK state.
/// Other SDK categories and events are deliberately ignored.
/// </summary>
internal sealed class PostHogDeliveryLoggerFactory(Action<ExceptionDeliveryFailure> report) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new DeliveryLogger(categoryName, report);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    private sealed class DeliveryLogger(string category, Action<ExceptionDeliveryFailure> report) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) =>
            category is "PostHog.Library.AsyncBatchHandler" or "PostHog.PostHogClient" && logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            bool batchFailure = category == "PostHog.Library.AsyncBatchHandler" && eventId.Id == 500;
            bool flushFailure = category == "PostHog.PostHogClient" && eventId.Id == 24 &&
                state is IEnumerable<KeyValuePair<string, object?>> values &&
                values.Any(pair => pair.Key == "MethodName" && pair.Value is "FlushAsync");
            ExceptionDeliveryFailure? failure = (category, eventId.Id) switch
            {
                ("PostHog.Library.AsyncBatchHandler", 111) => new("sdk_queue_drop_oldest"),
                _ when (batchFailure || flushFailure) && exception is ApiException api => new("http_failure", (int)api.Status),
                _ when (batchFailure || flushFailure) && exception is HttpRequestException http => new("http_failure", (int?)http.StatusCode),
                _ when batchFailure => new("batch_exception"),
                _ when flushFailure => new("flush_exception"),
                _ => null
            };
            try
            {
                if (failure is not null) report(failure);
            }
#pragma warning disable CA1031 // An observer must not disrupt the SDK's background worker.
            catch (Exception) { }
#pragma warning restore CA1031
        }
    }
}
