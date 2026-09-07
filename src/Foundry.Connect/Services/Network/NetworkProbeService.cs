// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

public interface INetworkProbeService
{
    Task<NetworkProbeResult> ProbeAsync(CancellationToken cancellationToken);
}

/// <summary>Checks exact bounded HTTP evidence without accepting redirects or login pages as connectivity.</summary>
public sealed class NetworkProbeService(InternetProbeOptions options, HttpClient client,
    ILogger<NetworkProbeService> logger) : INetworkProbeService, IDisposable
{
    public async Task<NetworkProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InternetProbeOptions normalized = options.Normalize();
        if (normalized.ConfigurationErrorCode is not null)
        {
            logger.LogWarning("Connectivity probe configuration is invalid. Endpoints={Endpoints}",
                string.Join(", ", normalized.InvalidEndpointUris));
            return new(NetworkReadinessStatus.Unavailable, normalized.ConfigurationErrorCode);
        }

        NetworkProbeResult result = new(NetworkReadinessStatus.Unavailable, "network_probe_unavailable");
        foreach (var endpoint in normalized.Probes!)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(normalized.TimeoutSeconds));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Uri);
                using HttpResponseMessage response = await client.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
                    return new(NetworkReadinessStatus.ProxyAuthenticationRequired, "network_proxy_authentication_required");
                if ((int)response.StatusCode != endpoint.ExpectedStatusCode || response.Content.Headers.ContentLength > 4096)
                    return new(NetworkReadinessStatus.UnexpectedResponse, "network_probe_unexpected_response");

                await using Stream body = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
                byte[] bytes = new byte[4097];
                int count = 0;
                while (count < bytes.Length)
                {
                    int read = await body.ReadAsync(bytes.AsMemory(count), budget.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    count += read;
                }
                byte[] expected = Encoding.UTF8.GetBytes(endpoint.ExpectedBody);
                if (count > 4096 || (response.Content.Headers.ContentLength is long length && length != count) ||
                    !bytes.AsSpan(0, count).SequenceEqual(expected))
                    return new(NetworkReadinessStatus.UnexpectedResponse, "network_probe_unexpected_response");
                return new(NetworkReadinessStatus.Online, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException)
            {
                result = new(NetworkReadinessStatus.TimedOut, "network_probe_timeout");
            }
            catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.NameResolutionError)
            {
                result = new(NetworkReadinessStatus.NameResolutionFailed, "network_dns_failed");
            }
            catch (HttpRequestException)
            {
                result = new(NetworkReadinessStatus.Unavailable, "network_probe_unavailable");
            }
            catch (IOException)
            {
                result = new(NetworkReadinessStatus.Unavailable, "network_probe_unavailable");
            }
        }
        return result;
    }

    public void Dispose() => client.Dispose();
}
