// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http;
using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;

namespace Foundry.Connect.Tests.Network;

public sealed class ConnectivityProbeTests
{
    private static HttpResponseMessage Text(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task Check_WhenBodyMatches_IsOnline()
    {
        var handler = new StubHttpMessageHandler((request, _) => Task.FromResult(request.RequestUri!.Host.Contains("msftconnecttest", StringComparison.Ordinal)
            ? Text(HttpStatusCode.OK, "Microsoft Connect Test")
            : Text(HttpStatusCode.OK, "Microsoft NCSI")));
        using var probe = new ConnectivityProbe(handler, new ManualTimeProvider(), new RecordingLogger<ConnectivityProbe>());

        Assert.Equal(InternetStatus.Online, await probe.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Check_WhenBodyDiffers_IsCaptivePortal()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Text(HttpStatusCode.OK, "<html><body>Guest Wi-Fi sign in</body></html>")));
        using var probe = new ConnectivityProbe(handler, new ManualTimeProvider(), new RecordingLogger<ConnectivityProbe>());

        Assert.Equal(InternetStatus.CaptivePortal, await probe.CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Check_WhenRedirected_DoesNotFollowAndIsCaptivePortal()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://portal.example.invalid/login");
            return Task.FromResult(response);
        });
        using var probe = new ConnectivityProbe(handler, new ManualTimeProvider(), new RecordingLogger<ConnectivityProbe>());

        Assert.Equal(InternetStatus.CaptivePortal, await probe.CheckAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.RequestedUris, static uri => uri.Host == "portal.example.invalid");

        using SocketsHttpHandler production = ConnectivityProbe.CreateHandler();
        Assert.False(production.AllowAutoRedirect);
        Assert.False(production.UseCookies);
        Assert.Equal(TimeSpan.Zero, production.PooledConnectionLifetime);
    }

    [Fact]
    public async Task Check_WhenNoEndpointAnswersBeforeTimeout_IsNoInternet()
    {
        var clock = new ManualTimeProvider();
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });
        using var probe = new ConnectivityProbe(handler, clock, new RecordingLogger<ConnectivityProbe>());

        Task<InternetStatus> check = probe.CheckAsync(TestContext.Current.CancellationToken);
        await AsyncAssert.EventuallyAsync(() => handler.RequestedUris.Count == 2, "both endpoints are probed in parallel");
        clock.Advance(ConnectivityProbe.RequestTimeout);

        Assert.Equal(InternetStatus.NoInternet, await check.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Check_RequestsOnlyNcsiEndpointsWithoutCache()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.True(request.Headers.CacheControl?.NoCache);
            Assert.True(request.Headers.ConnectionClose);
            return Task.FromResult(Text(HttpStatusCode.OK, "unexpected"));
        });
        using var probe = new ConnectivityProbe(handler, new ManualTimeProvider(), new RecordingLogger<ConnectivityProbe>());

        await probe.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["www.msftconnecttest.com", "www.msftncsi.com"],
            handler.RequestedUris.Select(static uri => uri.Host).Order(StringComparer.Ordinal));
    }
}
