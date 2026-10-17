// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using Foundry.Core.Services.Networking;

namespace Foundry.Core.Tests.Networking;

public sealed class ApplicationProxyTests
{
    [Fact]
    public void CreateManual_UsesConfiguredEndpointAndBypassRules()
    {
        IWebProxy proxy = ApplicationProxyFactory.CreateManual(
            "proxy.contoso.com",
            8080,
            bypassLocal: true,
            "*.contoso.com;downloads.example.net",
            CredentialCache.DefaultNetworkCredentials);

        Assert.Equal(new Uri("http://proxy.contoso.com:8080"), proxy.GetProxy(new Uri("https://github.com")));
        Assert.True(proxy.IsBypassed(new Uri("http://intranet")));
        Assert.True(proxy.IsBypassed(new Uri("https://packages.contoso.com")));
        Assert.True(proxy.IsBypassed(new Uri("https://downloads.example.net")));
        Assert.Equal(new Uri("https://packages.contoso.com"), proxy.GetProxy(new Uri("https://packages.contoso.com")));
        Assert.Same(CredentialCache.DefaultNetworkCredentials, proxy.Credentials);
    }

    [Theory]
    [InlineData(null, 8080, ApplicationProxyValidationReason.AddressRequired)]
    [InlineData("   ", 8080, ApplicationProxyValidationReason.AddressRequired)]
    [InlineData("not a host/path", 8080, ApplicationProxyValidationReason.InvalidAddress)]
    [InlineData("ftp://proxy.contoso.com", 8080, ApplicationProxyValidationReason.InvalidAddress)]
    [InlineData("user@proxy.contoso.com", 8080, ApplicationProxyValidationReason.InvalidAddress)]
    [InlineData("proxy.contoso.com", 0, ApplicationProxyValidationReason.InvalidPort)]
    [InlineData("proxy.contoso.com", 65536, ApplicationProxyValidationReason.InvalidPort)]
    public void CreateManual_RejectsInvalidConfigurationWithReason(
        string? address,
        int port,
        ApplicationProxyValidationReason expectedReason)
    {
        ApplicationProxyValidationException exception = Assert.Throws<ApplicationProxyValidationException>(
            () => ApplicationProxyFactory.CreateManual(address, port, bypassLocal: false, string.Empty, credentials: null));

        Assert.Equal(expectedReason, exception.Reason);
    }

    [Fact]
    public void MutableProxy_UsesUpdatedPolicyForExistingReference()
    {
        var initial = new StubProxy(new Uri("http://old-proxy:8080"));
        var updated = new StubProxy(new Uri("http://new-proxy:8080"));
        var proxy = new MutableApplicationProxy(initial);
        IWebProxy existingReference = proxy;

        Assert.Equal(initial.Endpoint, existingReference.GetProxy(new Uri("https://github.com")));

        proxy.Update(updated);

        Assert.Equal(updated.Endpoint, existingReference.GetProxy(new Uri("https://github.com")));
    }

    [Fact]
    public void MutableProxy_WhenActivePolicyHasNoProxy_PreservesNullResult()
    {
        var proxy = new MutableApplicationProxy(new NoProxyAvailablePolicy());
        var destination = new Uri("https://eu.i.posthog.com/i/v0/e/");

        Assert.False(proxy.IsBypassed(destination));
        Assert.Null(proxy.GetProxy(destination));
    }

    [Fact]
    public void MutableProxy_RejectsSelfReference()
    {
        var proxy = new MutableApplicationProxy(new StubProxy(new Uri("http://system-proxy:8080")));

        Assert.Throws<ArgumentException>(() => proxy.Update(proxy));
    }

    private sealed class StubProxy(Uri endpoint) : IWebProxy
    {
        public Uri Endpoint { get; } = endpoint;

        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => Endpoint;

        public bool IsBypassed(Uri host) => false;
    }

    private sealed class NoProxyAvailablePolicy : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) => null;

        public bool IsBypassed(Uri host) => false;
    }
}
