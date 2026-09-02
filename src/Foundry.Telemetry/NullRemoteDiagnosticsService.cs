// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Events;

namespace Foundry.Telemetry;

/// <summary>
/// Implements disabled remote diagnostics without allocating transport resources.
/// </summary>
public sealed class NullRemoteDiagnosticsService : IRemoteDiagnosticsService, IDisposable
{
    /// <summary>
    /// Gets the shared disabled service.
    /// </summary>
    public static NullRemoteDiagnosticsService Instance { get; } = new();

    private NullRemoteDiagnosticsService()
    {
    }

    /// <inheritdoc />
    public void Configure(RemoteDiagnosticsOptions options, RemoteDiagnosticsContext context)
    {
    }

    /// <inheritdoc />
    public void Disable()
    {
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
