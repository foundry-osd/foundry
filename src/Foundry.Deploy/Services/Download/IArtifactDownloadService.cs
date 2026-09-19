// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Download;

public interface IArtifactDownloadService
{
    /// <summary>
    /// Downloads or reuses an artifact, checking its bytes against <paramref name="expectedHash"/>
    /// when supplied by trusted catalog metadata. Cache sidecars are not trusted or required.
    /// Hashless artifacts retain legacy reuse behavior without an integrity guarantee.
    /// Downloads are staged beside the destination and published only after available size and hash checks pass.
    /// When <paramref name="allowDownload"/> is false, only cache reuse is allowed; a cache miss throws
    /// <see cref="IOException"/> without starting a transfer or changing the existing file.
    /// </summary>
    Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedHash = null,
        long? expectedSizeBytes = null,
        string? artifactKind = null,
        CancellationToken cancellationToken = default,
        IProgress<DownloadProgress>? progress = null,
        bool allowDownload = true);
}
