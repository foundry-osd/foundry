// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Utilities.Networking;

namespace Foundry.Core.Services.Catalog;

public sealed class VerifiedCatalogSnapshotAcquirer
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new(new ValidatedRedirectHandler(
        new HttpClientHandler { AllowAutoRedirect = false }, ValidateEndpoint))
    { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient client;

    public VerifiedCatalogSnapshotAcquirer() : this(SharedClient) { }
    public VerifiedCatalogSnapshotAcquirer(HttpClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<VerifiedCatalogDocument> AcquireAsync(string id, Uri sourceUri, CancellationToken cancellationToken = default)
    {
        if (sourceUri is null || !sourceUri.IsAbsoluteUri || sourceUri.AbsoluteUri != VerifiedCatalogSources.GetUri(id).AbsoluteUri)
            throw new InvalidDataException("The catalog source is not approved.");
        byte[] bytes = await HttpRetry.ExecuteAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpResponseException(response.StatusCode, response.Headers.RetryAfter?.Delta, response.Headers.RetryAfter?.Date);
            if (response.RequestMessage?.RequestUri is { } finalUri && finalUri.AbsoluteUri != sourceUri.AbsoluteUri)
                throw new InvalidDataException("The catalog response came from another source.");
            long? length = response.Content.Headers.ContentLength;
            if (length > MaximumBytes) throw new InvalidDataException("Catalog metadata exceeds the 32 MiB limit.");
            await using Stream input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            while (true)
            {
                int count = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0) break;
                if (output.Length + count > MaximumBytes) throw new InvalidDataException("Catalog metadata exceeds the 32 MiB limit.");
                output.Write(buffer, 0, count);
            }
            if (output.Length == 0 || (length is not null && output.Length != length))
                throw new InvalidDataException("Catalog metadata is empty or truncated.");
            return output.ToArray();
        }, new HttpRetryOptions(3, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)), cancellationToken).ConfigureAwait(false);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        var document = new VerifiedCatalogDocument(id, bytes, hash, "sha256:" + hash.ToLowerInvariant(), sourceUri, DateTimeOffset.UtcNow);
        _ = VerifiedCatalogContent.Parse(document);
        return document;
    }

    private static void ValidateEndpoint(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.AbsoluteUri != VerifiedCatalogSources.GetUri(VerifiedCatalogSources.OperatingSystems).AbsoluteUri &&
            uri.AbsoluteUri != VerifiedCatalogSources.GetUri(VerifiedCatalogSources.DriverPacks).AbsoluteUri)
            throw new InvalidDataException("The catalog redirect is not approved.");
    }
}
