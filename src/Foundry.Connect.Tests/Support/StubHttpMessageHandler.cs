// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;

namespace Foundry.Connect.Tests.Support;

/// <summary>
/// Message handler that records the requested URIs and answers through a delegate; no socket is ever opened.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    // Guarded by lock (RequestedUris).
    public List<Uri> RequestedUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (RequestedUris)
        {
            RequestedUris.Add(request.RequestUri!);
        }

        return respond(request, cancellationToken);
    }
}
