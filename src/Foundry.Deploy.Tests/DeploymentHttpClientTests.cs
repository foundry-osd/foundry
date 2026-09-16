// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentHttpClientTests
{
    public static TheoryData<Type, int> Clients => new()
    {
        { typeof(OperatingSystemCatalogService), 60 },
        { typeof(DriverPackCatalogService), 60 },
        { typeof(ArtifactDownloadService), 30 },
        { typeof(MicrosoftUpdateCatalogClient), 5 }
    };

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task DefaultClient_RejectsUntrustedCertificatesBeforeReadingPayload(Type serviceType, int timeoutMinutes)
    {
        HttpClient client = GetDefaultClient(serviceType);
        Assert.Equal(TimeSpan.FromMinutes(timeoutMinutes), client.Timeout);

        foreach (string certificateKind in new[] { "self-signed", "expired", "wrong-host" })
        {
            await using var server = new LoopbackServer(certificateKind);
            HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetStringAsync(server.Address, server.CancellationToken));

            Assert.Equal(HttpRequestError.SecureConnectionError, exception.HttpRequestError);
            Assert.Equal(HttpConnectionFailure.SecureConnectionMessage, exception.Message);
            HttpRequestException original = Assert.IsType<HttpRequestException>(exception.InnerException);
            Assert.IsType<AuthenticationException>(original.InnerException);
            Assert.False(server.PayloadRequested);
        }
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task DefaultClient_CanReadOrdinaryResponses(Type serviceType, int timeoutMinutes)
    {
        HttpClient client = GetDefaultClient(serviceType);
        await using var server = new LoopbackServer();

        string result = await client.GetStringAsync(server.Address, server.CancellationToken);

        Assert.Equal(LoopbackServer.Payload, result);
        Assert.Equal(TimeSpan.FromMinutes(timeoutMinutes), client.Timeout);
    }

    [Fact]
    public async Task DownloadAsync_WhenCertificateIsUntrusted_DoesNotPublishPayload()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("foundry-tls-download-");
        try
        {
            await using var server = new LoopbackServer("self-signed");
            var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance);
            string destination = Path.Combine(directory.FullName, "driver.exe");

            HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAsync(
                server.Address.ToString(), destination, cancellationToken: server.CancellationToken));

            Assert.Equal(HttpRequestError.SecureConnectionError, exception.HttpRequestError);
            Assert.False(File.Exists(destination));
            Assert.False(server.PayloadRequested);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ConnectionFailure_PreservesOtherErrorsAndExplainsTlsFailures()
    {
        var failure = new HttpRequestException(HttpRequestError.SecureConnectionError, "Platform-specific TLS message");
        Assert.Equal(HttpConnectionFailure.SecureConnectionMessage, HttpConnectionFailure.GetMessage(failure));
        var ordinary = new HttpRequestException("Connection refused");
        Assert.Equal(ordinary.Message, HttpConnectionFailure.GetMessage(ordinary));
    }

    // Inspect the actual long-lived clients so a caller reverting to an unsafe handler is covered.
    internal static HttpClient GetDefaultClient(Type serviceType)
    {
        FieldInfo? field = serviceType.GetField(serviceType == typeof(ArtifactDownloadService) ? "DefaultHttpClient" : "HttpClient", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<HttpClient>(field.GetValue(null));
    }

    private sealed class LoopbackServer : IAsyncDisposable
    {
        internal const string Payload = "untrusted catalog or executable payload";
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private readonly X509Certificate2? _certificate;
        private readonly Task _request;

        public LoopbackServer(string? certificateKind = null)
        {
            _lifetime.CancelAfter(TimeSpan.FromSeconds(15));
            if (certificateKind is not null)
            {
                using RSA key = RSA.Create(2048);
                var request = new CertificateRequest("CN=Foundry TLS test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder();
                if (certificateKind == "wrong-host")
                {
                    names.AddDnsName("wrong-host.invalid");
                }
                else
                {
                    names.AddIpAddress(IPAddress.Loopback);
                }

                request.CertificateExtensions.Add(names.Build());
                DateTimeOffset now = DateTimeOffset.UtcNow;
                using X509Certificate2 certificate = request.CreateSelfSigned(now.AddDays(-2), certificateKind == "expired" ? now.AddDays(-1) : now.AddDays(1));
                // Schannel servers need a temporary persisted key. Disposal removes it; no trust store is changed.
                _certificate = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);
            }

            _listener.Start();
            Address = new Uri($"{(_certificate is null ? "http" : "https")}://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/payload");
            _request = ServeAsync();
        }

        public Uri Address { get; }
        public CancellationToken CancellationToken => _lifetime.Token;
        public bool PayloadRequested { get; private set; }

        private async Task ServeAsync()
        {
            try
            {
                using TcpClient connection = await _listener.AcceptTcpClientAsync(_lifetime.Token);
                using NetworkStream network = connection.GetStream();
                using SslStream? tls = _certificate is null ? null : new SslStream(network, leaveInnerStreamOpen: true);
                if (tls is not null)
                {
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _certificate }, _lifetime.Token);
                }

                Stream stream = tls is null ? network : tls;
                using var reader = new StreamReader(stream, leaveOpen: true);
                string? line = await reader.ReadLineAsync(_lifetime.Token);
                if (line is null)
                {
                    return;
                }

                PayloadRequested = true;
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_lifetime.Token)))
                {
                }

                byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {Payload.Length}\r\nConnection: close\r\n\r\n{Payload}");
                await stream.WriteAsync(response, _lifetime.Token);
            }
            catch (Exception exception) when (exception is AuthenticationException or IOException or OperationCanceledException or SocketException)
            {
                // Certificate rejection closes the connection before an HTTP request can arrive.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _lifetime.CancelAsync();
            _listener.Stop();
            await _request;
            _certificate?.Dispose();
            _lifetime.Dispose();
        }
    }
}
