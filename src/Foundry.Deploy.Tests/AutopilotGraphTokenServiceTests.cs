using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Foundry.Deploy.Services.Autopilot;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotGraphTokenServiceTests
{
    [Fact]
    public async Task AcquireAccessTokenAsync_WhenTransientFailureOccurs_RetriesWithCertificateAssertion()
    {
        var handler = new RecordingTokenHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, """{ "error": "temporarily_unavailable" }""");
        handler.Enqueue(HttpStatusCode.OK, """{ "access_token": "graph-token", "token_type": "Bearer" }""");
        using X509Certificate2 certificate = CreateCertificate();
        var service = new AutopilotGraphTokenService(
            new HttpClient(handler),
            NullLogger<AutopilotGraphTokenService>.Instance,
            new AutopilotGraphTokenServiceOptions
            {
                RetryDelay = TimeSpan.Zero
            });

        string token = await service.AcquireAccessTokenAsync(
            "tenant-id",
            "client-id",
            certificate,
            CancellationToken.None);

        Assert.Equal("graph-token", token);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token", request.Uri.ToString());
            Assert.Contains("client_assertion_type=urn%3Aietf%3Aparams%3Aoauth%3Aclient-assertion-type%3Ajwt-bearer", request.Body, StringComparison.Ordinal);
            Assert.Contains("scope=https%3A%2F%2Fgraph.microsoft.com%2F.default", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE", request.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", request.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void CreateClientAssertion_UsesPs256AndSha256CertificateThumbprint()
    {
        using X509Certificate2 certificate = CreateCertificate();

        string assertion = AutopilotGraphTokenService.CreateClientAssertion(
            "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token",
            "client-id",
            certificate);

        string[] parts = assertion.Split('.');
        Assert.Equal(3, parts.Length);
        string headerJson = DecodeBase64Url(parts[0]);
        Assert.Contains("\"alg\":\"PS256\"", headerJson, StringComparison.Ordinal);
        Assert.Contains("\"x5t#S256\"", headerJson, StringComparison.Ordinal);
        Assert.DoesNotContain(certificate.ExportCertificatePem(), assertion, StringComparison.Ordinal);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Foundry OSD Autopilot Registration",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMonths(12));
    }

    private static string DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        int padding = (4 - base64.Length % 4) % 4;
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64.PadRight(base64.Length + padding, '=')));
    }

    private sealed class RecordingTokenHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new();

        public List<RecordedTokenRequest> Requests { get; } = [];

        public void Enqueue(HttpStatusCode statusCode, string body)
        {
            responses.Enqueue(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedTokenRequest(
                request.RequestUri!,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken)));

            return responses.Dequeue();
        }
    }

    private sealed record RecordedTokenRequest(Uri Uri, string Body);
}
