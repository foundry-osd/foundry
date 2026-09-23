// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Storage;

namespace Foundry.Core.Services.WinPe;

/// <summary>Maps verified byte counts to authoring progress without claiming a network transfer.</summary>
internal sealed class CacheVerificationProgress(IProgress<WinPeDownloadProgress>? progress, string fileName)
    : IProgress<CachedArtifactVerificationProgress>
{
    public void Report(CachedArtifactVerificationProgress value) => progress?.Report(new WinPeDownloadProgress
    {
        Percent = value.IsComplete ? 100 : (int)Math.Min(99, value.BytesVerified * 100.0 / value.TotalBytes),
        Status = $"Verifying cached source '{fileName}'."
    });
}
