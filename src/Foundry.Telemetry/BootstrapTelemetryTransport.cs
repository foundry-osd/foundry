// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>Only an actual transport receipt may acknowledge a durable destination.</summary>
internal interface IBootstrapTelemetryTransport : IAsyncDisposable
{
    Task<bool> DeliverAsync(BootstrapPendingRecord record, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>Reuses the existing HTTP analytics and buffered diagnostics clients without treating enqueue as receipt.</summary>
internal sealed class BootstrapTelemetryTransport : IBootstrapTelemetryTransport
{
    private readonly HttpClient _httpClient = new();
    private readonly PostHogTelemetryService _analytics;
    private readonly RemoteDiagnosticsOptions _options;
    private PostHogDiagnosticsExporter? _diagnostics;

    internal BootstrapTelemetryTransport(TelemetryOptions usage, TelemetryContext context,
        RemoteDiagnosticsOptions diagnostics)
    {
        _analytics = new PostHogTelemetryService(_httpClient, usage, context);
        _options = diagnostics;
    }

    public async Task<bool> DeliverAsync(BootstrapPendingRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (record.Destination == BootstrapTelemetryDestination.Analytics)
        {
            return await _analytics.SendDurableAsync(record.Id, record.Timestamp, record.Properties!, cancellationToken)
                .ConfigureAwait(false);
        }

        if (record.Destination != BootstrapTelemetryDestination.Exception)
            throw new ArgumentException("Bootstrap Logs use the shared reliable log pipeline.", nameof(record));
        _diagnostics ??= new PostHogDiagnosticsExporter(_options);
        _diagnostics.ExportException(record.Diagnostic!);
        // The exception SDK only enqueues; its flush method does not expose per-record receipts.
        return false;
    }

    public Task FlushAsync(CancellationToken cancellationToken) =>
        _diagnostics?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        if (_diagnostics is not null)
            await _diagnostics.DisposeAsync().ConfigureAwait(false);
    }
}
