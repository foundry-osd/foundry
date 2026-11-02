// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Cache;

/// <summary>
/// Shares the session that the startup coordinator selects before the wizard opens with the download and content services.
/// </summary>
public interface INetworkCacheSessionAccessor
{
    /// <summary>
    /// Gets or sets the session of the running deployment; only the startup coordinator sets it. <see langword="null"/> before
    /// startup resolves the network cache, while a Retry resolves it again, and in debug-safe mode.
    /// </summary>
    NetworkCacheSession? Current { get; set; }
}

public sealed class NetworkCacheSessionAccessor : INetworkCacheSessionAccessor
{
    private NetworkCacheSession? current;

    public NetworkCacheSession? Current
    {
        get => Volatile.Read(ref current);
        set => Volatile.Write(ref current, value);
    }
}
