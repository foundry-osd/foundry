// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Networking;

/// <summary>
/// Stops inactive HTTP transfers, including streamed content, and distinguishes timeouts from caller cancellation.
/// </summary>
public static class HttpTransfer
{
    /// <summary>
    /// Runs a transfer with a two-minute inactivity deadline reset after bytes are copied, allowing any total duration while progressing.
    /// The delegate must await all work and pass its supplied token to requests, reads and writes.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Action, Task<T>> transfer,
        CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        cancellationToken.ThrowIfCancellationRequested();
        timeProvider ??= TimeProvider.System;
        using var inactivity = new CancellationTokenSource(TimeSpan.FromMinutes(2), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, inactivity.Token);
        try
        {
            T result = await transfer(linked.Token, () => inactivity.CancelAfter(TimeSpan.FromMinutes(2))).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            string reason = inactivity.IsCancellationRequested ? "transfer inactivity deadline (2 minutes)"
                : "HTTP request deadline";
            throw new TimeoutException($"The {reason} was exceeded.", exception);
        }
    }
}
