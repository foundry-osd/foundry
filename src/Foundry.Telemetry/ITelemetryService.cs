namespace Foundry.Telemetry;

/// <summary>
/// Captures low-volume anonymous product telemetry for Foundry runtimes.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Captures a telemetry event after applying the shared privacy allowlist.
    /// </summary>
    /// <param name="eventName">Stable event name defined by the telemetry contract.</param>
    /// <param name="properties">Candidate event properties before allowlist filtering.</param>
    /// <param name="cancellationToken">Token used by callers to stop waiting for local telemetry work.</param>
    /// <returns>A task that completes when local capture work is finished.</returns>
    Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes queued telemetry when the host is about to exit or a final workflow event should be sent promptly.
    /// </summary>
    /// <param name="cancellationToken">Token used by callers to stop waiting for the best-effort flush.</param>
    /// <returns>A task that completes when the best-effort flush finishes or is abandoned.</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
