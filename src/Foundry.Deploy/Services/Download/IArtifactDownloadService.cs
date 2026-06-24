// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

public interface IArtifactDownloadService
{
    Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedHash = null,
        long? expectedSizeBytes = null,
        string? artifactKind = null,
        CancellationToken cancellationToken = default,
        IProgress<DownloadProgress>? progress = null);
}
