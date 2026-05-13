namespace Foundry.Telemetry;

/// <summary>
/// Provides the disabled telemetry implementation used when telemetry is off or misconfigured.
/// </summary>
public sealed class NullTelemetryService : ITelemetryService
{
    /// <inheritdoc />
    public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
