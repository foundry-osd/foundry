// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>
/// Coordinates process-wide remote diagnostics initialization and bounded shutdown.
/// </summary>
public static class RemoteDiagnosticsLifecycle
{
    /// <summary>
    /// Applies the current consent and configuration to the process-wide diagnostics sink.
    /// </summary>
    public static void Initialize(
        IRemoteDiagnosticsService service,
        TelemetrySettings settings,
        TelemetryContext telemetryContext)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(telemetryContext);

        RemoteDiagnosticsOptions options = new(
            settings.IsRemoteDiagnosticsEnabled,
            settings.HostUrl,
            settings.ProjectToken,
            settings.InstallId);

        if (!options.CanSend)
        {
            RemoteDiagnosticsSink.Clear();
            service.Disable();
            return;
        }

        service.Configure(options, TelemetryContextFactory.CreateRemoteDiagnosticsContext(telemetryContext));
        RemoteDiagnosticsSink.SetService(service);
    }

    /// <summary>
    /// Stops forwarding new records, drains buffered diagnostics, and disposes the service.
    /// </summary>
    public static async Task ShutdownAsync(
        IRemoteDiagnosticsService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);

        try
        {
            await service.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            RemoteDiagnosticsSink.Clear();
            await service.DisposeAsync().ConfigureAwait(false);
        }
    }
}
