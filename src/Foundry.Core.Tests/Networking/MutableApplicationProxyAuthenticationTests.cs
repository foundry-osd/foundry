// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Foundry.Core.Services.Networking;

namespace Foundry.Core.Tests.Networking;

public sealed class MutableApplicationProxyAuthenticationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingClient_UsesCurrentCredentialsAfterPolicyChanges(bool initiallyAuthenticated)
    {
        await using var server = new ChallengeProxy();
        var proxy = new MutableApplicationProxy(CreatePolicy(server.Endpoint,
            initiallyAuthenticated ? new NetworkCredential("old", "secret") : null));
        using var client = new HttpClient(new HttpClientHandler { Proxy = proxy }) { Timeout = TimeSpan.FromSeconds(10) };

        await AssertResponseAsync(client, initiallyAuthenticated ? HttpStatusCode.OK : HttpStatusCode.ProxyAuthenticationRequired,
            initiallyAuthenticated ? "Basic b2xkOnNlY3JldA==" : string.Empty);

        proxy.Update(CreatePolicy(server.Endpoint, new NetworkCredential("new", "password")));
        await AssertResponseAsync(client, HttpStatusCode.OK, "Basic bmV3OnBhc3N3b3Jk");

        proxy.Update(CreatePolicy(server.Endpoint, null));
        await AssertResponseAsync(client, HttpStatusCode.ProxyAuthenticationRequired, string.Empty);

        proxy.Update(CreatePolicy(server.Endpoint, CredentialCache.DefaultNetworkCredentials));
        await AssertResponseAsync(client, HttpStatusCode.ProxyAuthenticationRequired, string.Empty);
    }

    [Fact]
    public async Task ExistingClient_DoesNotSendPreviousCredentialsToNewProxy()
    {
        await using var first = new ChallengeProxy();
        await using var second = new ChallengeProxy();
        var proxy = new MutableApplicationProxy(CreatePolicy(first.Endpoint, new NetworkCredential("old", "secret")));
        using var client = new HttpClient(new HttpClientHandler { Proxy = proxy }) { Timeout = TimeSpan.FromSeconds(10) };
        await AssertResponseAsync(client, HttpStatusCode.OK, "Basic b2xkOnNlY3JldA==");

        proxy.Update(CreatePolicy(second.Endpoint, new NetworkCredential("new", "password")));

        await AssertResponseAsync(client, HttpStatusCode.OK, "Basic bmV3OnBhc3N3b3Jk");
    }

    [Fact]
    public void CachedCredentials_DoNotSendNewCredentialsToPreviousEndpoint()
    {
        var previousEndpoint = new Uri("http://old-proxy:8080");
        var endpoint = new Uri("http://new-proxy:8080");
        var proxy = new MutableApplicationProxy(CreatePolicy(previousEndpoint, new NetworkCredential("old", "secret")));
        ICredentials credentials = proxy.Credentials!;
        proxy.GetProxy(new Uri("https://example.com"));

        proxy.Update(CreatePolicy(endpoint, new NetworkCredential("new", "password")));
        proxy.GetProxy(new Uri("https://example.com"));

        Assert.Null(credentials.GetCredential(previousEndpoint, "Basic"));
        Assert.Equal("new", credentials.GetCredential(endpoint, "Basic")?.UserName);
    }

    [Fact]
    public void CachedCredentials_UsesWindowsIdentityOnlyForIntegratedAuthentication()
    {
        var endpoint = new Uri("http://proxy:8080");
        var proxy = new MutableApplicationProxy(CreatePolicy(endpoint, new NetworkCredential("old", "secret")));
        ICredentials credentials = proxy.Credentials!;
        proxy.Update(CreatePolicy(endpoint, CredentialCache.DefaultNetworkCredentials));
        proxy.GetProxy(new Uri("https://example.com"));

        Assert.Same(CredentialCache.DefaultNetworkCredentials, credentials.GetCredential(endpoint, "Negotiate"));
        Assert.Same(CredentialCache.DefaultNetworkCredentials, credentials.GetCredential(endpoint, "NTLM"));
        Assert.Null(credentials.GetCredential(endpoint, "Basic"));
        Assert.Null(credentials.GetCredential(endpoint, "Digest"));
    }

    private static IWebProxy CreatePolicy(Uri endpoint, ICredentials? credentials) =>
        ApplicationProxyFactory.CreateManual(endpoint.Host, endpoint.Port, false, null, credentials);

    private static async Task AssertResponseAsync(HttpClient client, HttpStatusCode status, string authorization)
    {
        using HttpResponseMessage response = await client.GetAsync("http://download.invalid/package");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(authorization, await response.Content.ReadAsStringAsync());
    }

    private sealed class ChallengeProxy : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource shutdown = new();
        private readonly Task serverTask;

        public ChallengeProxy()
        {
            listener.Start();
            Endpoint = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            serverTask = ServeAsync();
        }

        public Uri Endpoint { get; }

        public async ValueTask DisposeAsync()
        {
            await shutdown.CancelAsync();
            listener.Stop();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
            finally
            {
                shutdown.Dispose();
            }
        }

        private async Task ServeAsync()
        {
            while (true)
            {
                using TcpClient connection = await listener.AcceptTcpClientAsync(shutdown.Token);
                using NetworkStream stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);
                string authorization = string.Empty;
                while (await reader.ReadLineAsync(shutdown.Token) is { Length: > 0 } line)
                {
                    if (line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase))
                    {
                        authorization = line["Proxy-Authorization:".Length..].Trim();
                    }
                }

                string status = authorization.Length == 0 ? "407 Proxy Authentication Required" : "200 OK";
                string response = $"HTTP/1.1 {status}\r\nProxy-Authenticate: Basic realm=\"test\"\r\nConnection: close\r\nContent-Length: {authorization.Length}\r\n\r\n{authorization}";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), shutdown.Token);
            }
        }
    }
}
