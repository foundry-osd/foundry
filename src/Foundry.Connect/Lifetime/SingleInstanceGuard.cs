// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Lifetime;

/// <summary>
/// Holds a named system mutex so only one Foundry.Connect process runs at a time. The guard is owned by the thread that
/// acquired it and must be disposed on that thread.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>
    /// The mutex name Foundry.Connect uses; the <c>Global\</c> prefix makes it visible across every session.
    /// </summary>
    public const string ConnectInstanceName = @"Global\Foundry.Connect";

    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// Tries to become the only instance named <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The system mutex name, including its <c>Global\</c> or <c>Local\</c> namespace prefix.</param>
    /// <param name="guard">The acquired guard, or <see langword="null"/> when another instance already holds the name.</param>
    /// <returns><see langword="true"/> when this call created and owns the named mutex.</returns>
    public static bool TryAcquire(string name, out SingleInstanceGuard? guard)
    {
        guard = null;
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists with access control from another security context, so another instance is running.
            return false;
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return false;
        }

        guard = new SingleInstanceGuard(mutex);
        return true;
    }

    /// <summary>
    /// Releases the instance name so a later Foundry.Connect process can start.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
