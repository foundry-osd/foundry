// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Telemetry;

/// <summary>Serializes HTTP admission with permanent consent revocation for one exporter lifetime.</summary>
public sealed class TelemetryConsentGeneration : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly CancellationToken token;
    private TaskCompletionSource drained = CompletedSource();
    private Task cancellationTask = Task.CompletedTask;
    private int active;
    private bool revoked;
    private bool cancellationFinished;
    private bool disposeRequested;
    private bool sourceDisposed;

    public TelemetryConsentGeneration() => token = cancellation.Token;

    /// <summary>Admits a request only before revocation. Every admission requires exactly one EndSend.</summary>
    public bool TryBeginSend(out CancellationToken revoked)
    {
        lock (gate)
        {
            revoked = token;
            if (this.revoked) return false;
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    /// <summary>Records actual transport completion, including completion after a caller has stopped waiting.</summary>
    public void EndSend()
    {
        lock (gate)
        {
            if (active == 0) throw new InvalidOperationException("No admitted telemetry send remains to complete.");
            if (--active == 0) drained.TrySetResult();
            DisposeSourceIfDrained();
        }
    }

    /// <summary>Closes admission permanently and requests best-effort cancellation outside the admission lock.</summary>
    public void Revoke()
    {
        lock (gate)
        {
            if (revoked) return;
            revoked = true;
        }
        cancellationTask = CancelAndObserveAsync();
    }

    private async Task CancelAndObserveAsync()
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception) { /* Cancellation callback failures must not reopen consent or escape optional telemetry. */ }
        finally
        {
            lock (gate)
            {
                cancellationFinished = true;
                DisposeSourceIfDrained();
            }
        }
    }

    /// <summary>Waits for admitted requests to finish; after Revoke, no new admission can extend this drain.</summary>
    public Task WaitForDrainAsync(CancellationToken cancellationToken = default)
    {
        lock (gate) return drained.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Revokes immediately; cancellation-source disposal is deferred until sends and callbacks finish.</summary>
    public void Dispose()
    {
        lock (gate) disposeRequested = true;
        Revoke();
        lock (gate) DisposeSourceIfDrained();
    }

    private void DisposeSourceIfDrained()
    {
        if (!disposeRequested || !cancellationFinished || active != 0 || sourceDisposed) return;
        sourceDisposed = true;
        cancellation.Dispose();
    }

    private static TaskCompletionSource CompletedSource()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult();
        return result;
    }
}
