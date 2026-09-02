// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;

namespace Foundry.Core.Services.Networking;

/// <summary>
/// Keeps a stable proxy reference while allowing its active policy to change.
/// </summary>
public sealed class MutableApplicationProxy : IWebProxy
{
    private IWebProxy current;

    public MutableApplicationProxy(IWebProxy initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        current = initial;
    }

    public ICredentials? Credentials
    {
        get => Volatile.Read(ref current).Credentials;
        set => Volatile.Read(ref current).Credentials = value;
    }

    public Uri GetProxy(Uri destination) => Volatile.Read(ref current).GetProxy(destination) ?? destination;

    public bool IsBypassed(Uri host) => Volatile.Read(ref current).IsBypassed(host);

    public void Update(IWebProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        Volatile.Write(ref current, proxy);
    }
}
