// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Foundry.Connect.Network.Policy;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network;

/// <summary>
/// Checks whether the active network reaches the internet.
/// </summary>
public interface IConnectivityProbe
{
    /// <summary>
    /// Probes the connectivity-test endpoints once.
    /// </summary>
    /// <param name="cancellationToken">Cancels the probe; only caller cancellation throws.</param>
    /// <returns>
    /// <see cref="InternetStatus.Online"/>, <see cref="InternetStatus.CaptivePortal"/> or <see cref="InternetStatus.NoInternet"/>.
    /// Network failures never throw.
    /// </returns>
    Task<InternetStatus> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>
/// NCSI-style probe: both <see cref="ConnectivityEvaluator.Endpoints"/> are requested in parallel without following
/// redirects, so a captive portal can never look like the internet (D2, NET-02).
/// </summary>
/// <remarks>
/// The client has no <see cref="HttpClient.Timeout"/> (it would surface as a <see cref="TaskCanceledException"/> that is hard
/// to tell from cancellation); each request owns a <see cref="RequestTimeout"/> deadline driven by the <see cref="TimeProvider"/>.
/// Connections are never pooled across probes so a network change cannot reuse a stale route.
/// </remarks>
public sealed class ConnectivityProbe : IConnectivityProbe, IDisposable
{
    /// <summary>Gets the time each endpoint has to answer completely.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Gets the maximum number of body bytes read from a response.</summary>
    public const int MaximumBodyBytes = 512;

    private readonly HttpClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConnectivityProbe> _logger;

    /// <summary>
    /// Creates a probe that sends real HTTP requests.
    /// </summary>
    /// <param name="timeProvider">The clock that drives the request deadlines.</param>
    /// <param name="logger">The logger.</param>
    public ConnectivityProbe(TimeProvider timeProvider, ILogger<ConnectivityProbe> logger)
        : this(CreateHandler(), timeProvider, logger)
    {
    }

    internal ConnectivityProbe(HttpMessageHandler handler, TimeProvider timeProvider, ILogger<ConnectivityProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InternetStatus> CheckAsync(CancellationToken cancellationToken)
    {
        ProbeObservation[] observations = await Task
            .WhenAll(ConnectivityEvaluator.Endpoints.Select(endpoint => ProbeAsync(endpoint, cancellationToken)))
            .ConfigureAwait(false);

        return ConnectivityEvaluator.Evaluate(observations);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.Zero
    };

    private async Task<ProbeObservation> ProbeAsync(NcsiEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        ProbeObservation observation;
        try
        {
            observation = await RequestAsync(endpoint, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            observation = new ProbeObservation(endpoint, false, null, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            observation = new ProbeObservation(endpoint, false, null, null);
        }

        _logger.LogDebug(
            "Connectivity endpoint probed. Endpoint={Endpoint} Responded={Responded} StatusCode={StatusCode}",
            endpoint.Uri.Host,
            observation.Responded,
            observation.StatusCode);
        return observation;
    }

    private async Task<ProbeObservation> RequestAsync(NcsiEndpoint endpoint, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.ConnectionClose = true;

        using HttpResponseMessage response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        string body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
        return new ProbeObservation(endpoint, true, (int)response.StatusCode, body);
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        byte[] buffer = new byte[MaximumBodyBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }
}
