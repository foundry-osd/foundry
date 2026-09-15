// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;

namespace Foundry.Deploy.Services.Http;

/// <summary>
/// Creates Deploy clients using the platform's certificate validation and proxy settings.
/// </summary>
public static class DeploymentHttpClientFactory
{
    /// <summary>
    /// Creates a client with the caller's timeout and standard HTTPS server authentication.
    /// </summary>
    public static HttpClient Create(TimeSpan timeout)
    {
        return new HttpClient(new SecureConnectionHandler())
        {
            Timeout = timeout
        };
    }

    private sealed class SecureConnectionHandler : DelegatingHandler
    {
        public SecureConnectionHandler() : base(new HttpClientHandler())
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception) when (HttpConnectionFailure.IsSecureConnectionFailure(exception))
            {
                // Startup diagnostics also need guidance without delaying Bootstrap's failure handoff.
                throw new HttpRequestException(exception.HttpRequestError, HttpConnectionFailure.SecureConnectionMessage, exception);
            }
        }
    }
}
