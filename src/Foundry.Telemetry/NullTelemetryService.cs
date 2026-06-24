// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
