// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

public interface IArtifactDownloadService
{
    /// <summary>
    /// Downloads or reuses an artifact, checking its bytes against <paramref name="expectedHash"/>
    /// when supplied by trusted catalog metadata. Cache sidecars are not trusted or required.
    /// Hashless artifacts retain legacy reuse behavior without an integrity guarantee.
    /// </summary>
    Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedHash = null,
        long? expectedSizeBytes = null,
        string? artifactKind = null,
        CancellationToken cancellationToken = default,
        IProgress<DownloadProgress>? progress = null);
}
