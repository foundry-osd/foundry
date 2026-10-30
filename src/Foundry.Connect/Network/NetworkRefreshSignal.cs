// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Network;

/// <summary>
/// Wakes the network monitor for an immediate observation, for example after a network operation ended.
/// </summary>
/// <remarks>
/// Requests coalesce: any number of requests made before the monitor waits produce a single wake.
/// </remarks>
public sealed class NetworkRefreshSignal : IDisposable
{
    private readonly SemaphoreSlim _pending = new(0, 1);

    /// <summary>
    /// Asks for one refresh. A request already pending absorbs this one.
    /// </summary>
    public void Request()
    {
        if (_pending.CurrentCount != 0)
        {
            return;
        }

        try
        {
            _pending.Release();
        }
        catch (SemaphoreFullException)
        {
            // Another caller released between the check and the release; one pending request is enough.
        }
    }

    /// <summary>
    /// Waits until a refresh is requested, consuming the pending request.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>A task that completes when a request is available.</returns>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        return _pending.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pending.Dispose();
    }
}
