// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Execution-only readiness evidence; never persisted as configuration or reused by another deployment.</summary>
internal sealed class DeploymentPreflightState : IDisposable
{
    public required string CacheRoot { get; init; }

    /// <summary>
    /// Gets whether the image archive is staged on the target after disk preparation: a catalog download, or a custom image
    /// on the network cache share, which is copied to the target and verified during the copy.
    /// </summary>
    public bool UsesTargetStorage { get; init; }
    public string? ExternalImageDirectory { get; init; }
    public long SourceSizeBytes { get; set; }
    public long TargetDriverBytes { get; set; }
    public WindowsImageMetadata? Image { get; set; }
    public string? ImagePath { get; set; }
    public FileStream? SourceLease { get; set; }
    public Services.Images.CustomImageSourceLease? CustomSourceLease { get; set; }
    public bool ErasureStarted { get; set; }

    /// <summary>Rejects storage decisions when the execution's resolved cache changes.</summary>
    public bool MatchesStoragePlan(DeploymentStepExecutionContext context) =>
        string.Equals(CacheRoot, context.RuntimeState.ResolvedCache?.RootPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rejects stale readiness after cache or prepared-image paths are changed. A custom image lease is always rechecked, including
    /// for a network cache share image staged on the target, whose lease moves from the share file to the verified target copy.
    /// </summary>
    public bool Matches(DeploymentStepExecutionContext context)
    {
        try
        {
            return MatchesStoragePlan(context) &&
                (CustomSourceLease is not null
                    ? Image is not null && CustomSourceLease.IsReadable() && CustomSourceLease.Length == SourceSizeBytes && MatchesImagePath(context)
                    : UsesTargetStorage || (Image is not null && SourceLease?.CanRead == true && SourceLease.Length == SourceSizeBytes && MatchesImagePath(context)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return false;
        }
    }

    private bool MatchesImagePath(DeploymentStepExecutionContext context) =>
        string.Equals(ImagePath, context.RuntimeState.DownloadedOperatingSystemPath, StringComparison.OrdinalIgnoreCase) && File.Exists(ImagePath);

    /// <summary>Keeps a verified external file readable and prevents replacement while it is needed.</summary>
    public void Dispose()
    {
        SourceLease?.Dispose();
        CustomSourceLease?.Dispose();
    }
}
