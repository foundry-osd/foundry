// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Services.Cache;

/// <summary>Classifies how the network cache was resolved for one deployment.</summary>
public enum NetworkCacheOutcome
{
    Disabled,
    NoMatchingScope,
    Connected,
    Unavailable
}

/// <summary>
/// Holds the share chosen for one deployment. Every consumer treats a session without a share as "behave as today".
/// Disposing closes the SMB session.
/// </summary>
public sealed class NetworkCacheSession : IDisposable
{
    private readonly ISmbConnector? connector;
    private bool disposed;
    private bool unresponsive;

    private NetworkCacheSession(NetworkCacheOutcome outcome, string? sharePath, bool canWrite, ISmbConnector? connector)
    {
        Outcome = outcome;
        SharePath = sharePath;
        CanWrite = canWrite;
        this.connector = connector;
    }

    public NetworkCacheOutcome Outcome { get; }

    /// <summary>Gets the connected UNC share, or <see langword="null"/> when no share is used.</summary>
    public string? SharePath { get; }

    /// <summary>Gets <c>&lt;share&gt;\Foundry</c>, the root of downloads and of imported content mirrored from media.</summary>
    public string? ContentRoot => SharePath is null ? null : Core.Services.NetworkCache.NetworkCacheLayout.ContentRoot(SharePath);

    /// <summary>Gets whether this device may publish downloads; only shares with an account that passed a write probe.</summary>
    public bool CanWrite { get; }

    /// <summary>Gets whether consumers may use the share; false once it stopped answering during the deployment.</summary>
    public bool IsAvailable => SharePath is not null && !Volatile.Read(ref unresponsive);

    /// <summary>Stops every later lookup and copy from waiting on a share that already timed out once.</summary>
    public void MarkUnresponsive() => Volatile.Write(ref unresponsive, true);

    public static NetworkCacheSession Unavailable(NetworkCacheOutcome outcome) => new(outcome, null, false, null);

    internal static NetworkCacheSession Connected(string sharePath, bool canWrite, ISmbConnector connector) =>
        new(NetworkCacheOutcome.Connected, sharePath, canWrite, connector);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (SharePath is not null) connector?.Disconnect(SharePath);
    }
}
