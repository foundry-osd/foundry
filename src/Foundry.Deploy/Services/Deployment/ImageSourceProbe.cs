// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Localization;
using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Uses a bounded HTTP range request to check access, even when HEAD is unsupported.</summary>
public sealed class ImageSourceProbe : IImageSourceProbe
{
    private static readonly HttpClient DefaultHttpClient = DeploymentHttpClientFactory.Create(TimeSpan.FromSeconds(20));
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;

    /// <summary>Uses the deployment TLS and Windows Update connection policy.</summary>
    public ImageSourceProbe() : this(DefaultHttpClient, TimeSpan.FromSeconds(20)) { }

    internal ImageSourceProbe(HttpClient httpClient, TimeSpan? timeout = null)
    {
        _httpClient = httpClient;
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
    }

    /// <inheritdoc />
    public async Task<long?> ProbeAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, WindowsUpdateContentUrl.Normalize(sourceUrl));
        request.Headers.Range = new RangeHeaderValue(0, 0);
        try
        {
            using HttpResponseMessage response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            {
                throw Failure(DeploymentFailureReasons.HttpStatus);
            }

            long? length;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                ContentRangeHeaderValue? range = response.Content.Headers.ContentRange;
                if (range is null || !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase) || range.From != 0 || range.To != 0)
                {
                    throw Failure(DeploymentFailureReasons.InvalidPayload);
                }

                length = range.Length;
            }
            else
            {
                length = response.Content.Headers.ContentLength;
            }

            if (length is <= 0)
            {
                throw Failure(DeploymentFailureReasons.InvalidPayload);
            }

            // Do not buffer or read the response body: some content servers ignore Range entirely.
            return length;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(DeploymentFailureReasons.DeadlineExceeded);
        }
        catch (HttpRequestException exception)
        {
            throw new DeploymentOperationException(
                new DeploymentFailure(DeploymentOperationNames.ProbeOperatingSystemSource, DeploymentFailureKinds.Http,
                    DeploymentFailureReasons.TransportError, "image_source_unavailable"),
                HttpConnectionFailure.IsSecureConnectionFailure(exception)
                    ? HttpConnectionFailure.SecureConnectionMessage : LocalizationText.GetString("Preflight.SourceUnavailable"), exception);
        }
    }

    private static DeploymentOperationException Failure(string reason) => new(
        new DeploymentFailure(DeploymentOperationNames.ProbeOperatingSystemSource,
            reason == DeploymentFailureReasons.DeadlineExceeded ? DeploymentFailureKinds.Timeout : DeploymentFailureKinds.Http,
            reason, "image_source_unavailable"), LocalizationText.GetString("Preflight.SourceUnavailable"));
}
