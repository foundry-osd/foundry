// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Execution-only readiness evidence; never persisted as configuration or reused by another deployment.</summary>
internal sealed class DeploymentPreflightState : IDisposable
{
    public required string CacheRoot { get; init; }
    public bool UsesTargetStorage { get; init; }
    public long SourceSizeBytes { get; init; }
    public long TargetDriverBytes { get; init; }
    public WindowsImageMetadata? Image { get; init; }
    public string? ImagePath { get; init; }
    public FileStream? SourceLease { get; init; }
    public bool ErasureStarted { get; set; }

    /// <summary>Rejects stale readiness after cache or prepared-image paths are changed.</summary>
    public bool Matches(DeploymentStepExecutionContext context)
    {
        try
        {
            return string.Equals(CacheRoot, context.RuntimeState.ResolvedCache?.RootPath, StringComparison.OrdinalIgnoreCase) &&
                (UsesTargetStorage || (Image is not null && SourceLease?.CanRead == true && SourceLease.Length == SourceSizeBytes &&
                    string.Equals(ImagePath, context.RuntimeState.DownloadedOperatingSystemPath, StringComparison.OrdinalIgnoreCase) && File.Exists(ImagePath)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Keeps a verified external file readable and prevents replacement while it is needed.</summary>
    public void Dispose() => SourceLease?.Dispose();
}
