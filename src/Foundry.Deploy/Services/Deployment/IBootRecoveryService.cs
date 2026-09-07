// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>Configures boot and recovery using retained target identities and explicit native ownership.</summary>
public interface IBootRecoveryService
{
    /// <summary>Identifies unresolved servicing resources; callers must persist this evidence before another deployment.</summary>
    RecoveryResourceDiagnostic? RecoveryDiagnostic { get; }
    /// <summary>Revalidates the retained Windows and system partitions before configuring UEFI boot files.</summary>
    Task ConfigureBootAsync(DeploymentTargetLayout retainedLayout, string windowsPartitionRoot, string systemPartitionRoot,
        int operatingSystemBuildMajor, string workingDirectory, CancellationToken cancellationToken = default);
    /// <summary>Copies the applied image's WinRE image and registers its offline recovery location.</summary>
    Task ConfigureRecoveryEnvironmentAsync(string windowsPartitionRoot, string recoveryPartitionRoot,
        string workingDirectory, CancellationToken cancellationToken = default);
    /// <summary>Revalidates the retained recovery partition before removing its temporary drive letter.</summary>
    Task SealRecoveryPartitionAsync(DeploymentTargetLayout retainedLayout, string recoveryPartitionRoot,
        char recoveryPartitionLetter, string workingDirectory, CancellationToken cancellationToken = default);
    /// <summary>Services an owned WinRE mount and independently bounds cleanup, retaining uncertain resources and the primary failure.</summary>
    Task ApplyRecoveryDriversAsync(string recoveryPartitionRoot, string driverRoot, string scratchDirectory,
        string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? mountProgress = null,
        IProgress<double>? applyProgress = null, IProgress<double>? unmountProgress = null,
        Action? onMountStarted = null, Action? onApplyStarted = null, Action? onUnmountStarted = null);
}
