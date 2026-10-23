// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Cache;

/// <summary>Shares the session resolved by the cache strategy step with the download and content services.</summary>
public interface INetworkCacheSessionAccessor
{
    /// <summary>Gets or sets the session of the running deployment; <see langword="null"/> before the cache strategy step.</summary>
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
