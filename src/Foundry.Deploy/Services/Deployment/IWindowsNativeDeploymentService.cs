// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Runs documented native deployment operations with an isolated, operation-owned lifetime.</summary>
public interface IWindowsNativeDeploymentService
{
    /// <summary>Checks required native entry points before target mutation; does not initialize servicing.</summary>
    Task EnsureAvailableAsync(bool requiresWim, string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Returns typed feature states. Unknown native states fail closed.</summary>
    Task<IReadOnlyDictionary<string, OfflineWindowsFeatureState>> ReadFeatureStatesAsync(string windowsRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Disables a feature without removing its payload.</summary>
    Task DisableFeatureAsync(string windowsRoot, string featureName, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Adds each INF recursively with signed-driver enforcement and completed-file progress.</summary>
    Task AddDriversAsync(string windowsRoot, string driverRoot, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>Mounts image index one writable at an empty, owned mount directory.</summary>
    Task MountImageAsync(string imagePath, string mountPath, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>Commits or discards a registered image. Callers use a cleanup token independent from cancellation.</summary>
    Task UnmountImageAsync(string mountPath, bool commit, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default);

    /// <summary>Checks registrations before deleting mount contents; inventory failures never imply an absent mount.</summary>
    Task<bool> IsMountedAsync(string mountPath, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default);

    /// <summary>Applies an ordinary WIM with archive and file-data verification. ESD is handled explicitly by the caller.</summary>
    Task ApplyWimAsync(string imagePath, int imageIndex, string windowsRoot, string scratchDirectory, string workingDirectory, IProgress<double>? progress, CancellationToken cancellationToken = default);
}
