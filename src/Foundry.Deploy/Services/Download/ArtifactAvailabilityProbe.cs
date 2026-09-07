// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Networking;
using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Services.Download;

/// <summary>Collects bounded availability evidence; it never validates image bytes.</summary>
public sealed class ArtifactAvailabilityProbe : IArtifactAvailabilityProbe
{
    private readonly HttpClient? httpClient;
    private readonly TimeSpan timeout = TimeSpan.FromSeconds(30);
    private readonly DeploymentNetworkPolicy networkPolicy;

    /// <summary>Creates a probe using the validated production transport.</summary>
    public ArtifactAvailabilityProbe(DeploymentNetworkPolicy? networkPolicy = null) => this.networkPolicy = networkPolicy ?? new(false);
    internal ArtifactAvailabilityProbe(HttpClient httpClient, TimeSpan? timeout = null, DeploymentNetworkPolicy? networkPolicy = null)
        : this(networkPolicy)
    {
        this.httpClient = httpClient;
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public async Task EnsureAvailableAsync(ArtifactIdentity artifact, CancellationToken cancellationToken = default)
    {
        ArtifactIntegrityPolicy.Validate(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        networkPolicy.ThrowIfNetworkUnavailable();
        Uri source = artifact.Kind == "OperatingSystemImage" &&
            Path.GetExtension(artifact.FileName).Equals(".esd", StringComparison.OrdinalIgnoreCase)
            ? new Uri(WindowsUpdateContentUrl.Normalize(artifact.SourceUri.AbsoluteUri)) : artifact.SourceUri;
        ArtifactIntegrityPolicy.ValidateSourceUri(artifact, source);
        using HttpClient? ownedClient = httpClient is null
            ? AcquisitionHttpClientFactory.Create(Timeout.InfiniteTimeSpan, uri => ArtifactIntegrityPolicy.ValidateSourceUri(artifact, uri))
            : null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        try
        {
            using HttpResponseMessage response = await (httpClient ?? ownedClient!).SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
                throw new HttpRequestException("Artifact source availability check failed.", null, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.PartialContent &&
                (response.Content.Headers.ContentRange is not { From: 0 } range || !range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Artifact source returned an invalid availability range.");
            using Stream body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            byte[] firstByte = new byte[1];
            if (await body.ReadAsync(firstByte.AsMemory(), deadline.Token).ConfigureAwait(false) != 1)
                throw new InvalidDataException("Artifact source returned an empty availability response.");
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Artifact source availability check exceeded its deadline.", ex);
        }
    }
}
