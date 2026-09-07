// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Http;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

public interface IAutopilotGraphTokenService
{
    Task<string> AcquireAccessTokenAsync(
        string tenantId,
        string clientId,
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Acquires app-only Graph tokens with a certificate client assertion; no client secrets or interactive flows are supported.
/// </summary>
public sealed class AutopilotGraphTokenService(
    HttpClient httpClient,
    ILogger<AutopilotGraphTokenService> logger,
    AutopilotGraphTokenServiceOptions? options = null) : IAutopilotGraphTokenService
{
    private const string Scope = "https://graph.microsoft.com/.default";
    private const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly HttpClient httpClient = httpClient;
    private readonly ILogger logger = logger;
    private readonly AutopilotGraphTokenServiceOptions options = options ?? new AutopilotGraphTokenServiceOptions();

    public async Task<string> AcquireAccessTokenAsync(
        string tenantId,
        string clientId,
        X509Certificate2 certificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(certificate);

        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(options));
        string tokenEndpoint = $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId.Trim())}/oauth2/v2.0/token";
        string clientAssertion = CreateClientAssertion(tokenEndpoint, clientId.Trim(), certificate);

        return await HttpRetryPolicy.ExecuteAsync(
            async ct =>
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId.Trim(),
                    ["scope"] = Scope,
                    ["grant_type"] = "client_credentials",
                    ["client_assertion_type"] = ClientAssertionType,
                    ["client_assertion"] = clientAssertion
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint) { Content = content };
                using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                string responseBody = await BoundedHttpContent.ReadStringAsync(response, 32L * 1024 * 1024, ct).ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(responseBody);
                if (!document.RootElement.TryGetProperty("access_token", out JsonElement tokenElement) ||
                    string.IsNullOrWhiteSpace(tokenElement.GetString()))
                {
                    throw new InvalidOperationException("Microsoft Entra token response did not contain an access token.");
                }

                return tokenElement.GetString()!;
            },
            logger,
            "Autopilot Graph token acquisition",
            cancellationToken,
            HttpOperationOptions.Metadata with
            {
                MaximumAttempts = checked(options.RetryCount + 1),
                RequestTimeout = options.RequestTimeout,
                InitialRetryDelay = options.RetryDelay,
                MaximumRetryDelay = options.RetryDelay > HttpOperationOptions.Metadata.MaximumRetryDelay
                    ? options.RetryDelay : HttpOperationOptions.Metadata.MaximumRetryDelay
            }).ConfigureAwait(false);
    }

    internal static string CreateClientAssertion(
        string tokenEndpoint,
        string clientId,
        X509Certificate2 certificate)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["alg"] = "PS256",
            ["typ"] = "JWT",
            ["x5t#S256"] = Base64UrlEncode(SHA256.HashData(certificate.RawData))
        }, AutopilotGraphJson.Options);
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["aud"] = tokenEndpoint,
            ["exp"] = now + 600,
            ["iss"] = clientId,
            ["jti"] = Guid.NewGuid().ToString("D"),
            ["nbf"] = now,
            ["iat"] = now,
            ["sub"] = clientId
        }, AutopilotGraphJson.Options);

        string unsignedToken = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(header))}.{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}";
        using RSA? rsa = certificate.GetRSAPrivateKey();
        if (rsa is null)
        {
            throw new InvalidOperationException("The embedded PFX certificate does not expose an RSA private key.");
        }

        byte[] signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);
        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed record AutopilotGraphTokenServiceOptions
{
    public int RetryCount { get; init; } = HttpRetryPolicy.DefaultRetryCount;
    public TimeSpan RetryDelay { get; init; } = HttpRetryPolicy.DefaultRetryDelay;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
