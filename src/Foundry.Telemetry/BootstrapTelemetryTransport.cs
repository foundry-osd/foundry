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
    private readonly RemoteDiagnosticsContext _context;
    private readonly Dictionary<RemoteDiagnosticsContext, PostHogDiagnosticsExporter> _diagnostics = [];

    internal BootstrapTelemetryTransport(TelemetryOptions usage, TelemetryContext context,
        RemoteDiagnosticsOptions diagnostics, RemoteDiagnosticsContext diagnosticContext)
    {
        _analytics = new PostHogTelemetryService(_httpClient, usage, context);
        _options = diagnostics;
        _context = diagnosticContext;
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
        RemoteDiagnosticsContext context = GetResourceContext(record.Diagnostic!, _context);
        if (!_diagnostics.TryGetValue(context, out PostHogDiagnosticsExporter? exporter))
        {
            exporter = new PostHogDiagnosticsExporter(_options, context);
            _diagnostics.Add(context, exporter);
        }
        exporter.ExportException(record.Diagnostic!);
        // The exception SDK only enqueues; its flush method does not expose per-record receipts.
        return false;
    }

    internal static RemoteDiagnosticsContext GetResourceContext(RemoteDiagnosticRecord record, RemoteDiagnosticsContext fallback)
    {
        string Attribute(string name, string defaultValue) => record.Attributes.TryGetValue(name, out object? value) && value is string text
            ? text : defaultValue;
        return new RemoteDiagnosticsContext(Attribute("service.name", fallback.App), Attribute("service.version", fallback.AppVersion),
            string.Empty, Attribute("runtime.name", fallback.Runtime), Attribute("runtime.architecture", fallback.RuntimeArchitecture),
            string.Empty, string.Empty, Attribute("service.release", fallback.Release));
    }

    public Task FlushAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(_diagnostics.Values.Select(exporter => exporter.FlushAsync(cancellationToken)));

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        foreach (PostHogDiagnosticsExporter exporter in _diagnostics.Values)
            await exporter.DisposeAsync().ConfigureAwait(false);
    }
}
