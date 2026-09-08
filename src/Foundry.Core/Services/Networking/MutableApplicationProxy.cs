// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Net;

namespace Foundry.Core.Services.Networking;

/// <summary>
/// Keeps stable proxy and credential references while allowing the active policy to change.
/// </summary>
public sealed class MutableApplicationProxy : IWebProxy
{
    private ProxyPolicy current;
    private readonly ICredentials credentials;

    public MutableApplicationProxy(IWebProxy initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        current = new ProxyPolicy(initial);
        credentials = new CurrentProxyCredentials(this);
    }

    /// <summary>
    /// Resolves credentials at authentication time because HTTP handlers cache this reference.
    /// Setting credentials updates the active policy without replacing the stable reference.
    /// </summary>
    public ICredentials? Credentials
    {
        get => credentials;
        set => Volatile.Read(ref current).Proxy.Credentials = value;
    }

    public Uri? GetProxy(Uri destination)
    {
        ProxyPolicy policy = Volatile.Read(ref current);
        Uri? endpoint = policy.Proxy.GetProxy(destination);
        if (endpoint is not null && endpoint != destination)
        {
            policy.Endpoints.TryAdd(endpoint, 0);
        }

        return endpoint;
    }

    public bool IsBypassed(Uri host) => Volatile.Read(ref current).Proxy.IsBypassed(host);

    /// <summary>
    /// Applies a policy to subsequent routing and authentication lookups. Existing authenticated
    /// connections and authentication exchanges already in progress are not interrupted.
    /// </summary>
    public void Update(IWebProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (ReferenceEquals(this, proxy))
        {
            throw new ArgumentException("A mutable proxy cannot reference itself.", nameof(proxy));
        }

        Volatile.Write(ref current, new ProxyPolicy(proxy));
    }

    private sealed class ProxyPolicy(IWebProxy proxy)
    {
        public IWebProxy Proxy { get; } = proxy;

        // Track proxy endpoints, not destinations. Replacing the policy also drops old endpoints,
        // so a delayed challenge from an old proxy cannot receive the new policy's credentials.
        public ConcurrentDictionary<Uri, byte> Endpoints { get; } = new();
    }

    private sealed class CurrentProxyCredentials(MutableApplicationProxy owner) : ICredentials
    {
        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            ProxyPolicy policy = Volatile.Read(ref owner.current);
            if (!policy.Endpoints.ContainsKey(uri))
            {
                return null;
            }

            NetworkCredential? credential = policy.Proxy.Credentials?.GetCredential(uri, authType);
            if (ReferenceEquals(credential, CredentialCache.DefaultNetworkCredentials) &&
                !authType.Equals("Negotiate", StringComparison.OrdinalIgnoreCase) &&
                !authType.Equals("NTLM", StringComparison.OrdinalIgnoreCase) &&
                !authType.Equals("Kerberos", StringComparison.OrdinalIgnoreCase))
            {
                // The handler cannot recognize default credentials through this wrapper.
                // Windows identity is only valid for integrated authentication.
                return null;
            }

            return credential;
        }
    }
}
