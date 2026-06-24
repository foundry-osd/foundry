// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

public sealed record ArtifactDownloadResult
{
    public required string DestinationPath { get; init; }
    public required bool Downloaded { get; init; }
    public required string Method { get; init; }
    public long SizeBytes { get; init; }
}
