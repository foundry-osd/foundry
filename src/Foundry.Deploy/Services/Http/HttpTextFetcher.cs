// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Http;

public static class HttpTextFetcher
{
    public static Task<string> GetStringWithRetryAsync(
        HttpClient client,
        string requestUri,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        return HttpRetryPolicy.ExecuteAsync(
            ct => client.GetStringAsync(requestUri, ct),
            logger,
            operationName,
            cancellationToken);
    }
}
