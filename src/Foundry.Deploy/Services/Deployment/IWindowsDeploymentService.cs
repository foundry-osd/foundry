namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Defines the Windows deployment operations that mutate disks, offline images, boot files, and recovery images.
/// </summary>
public interface IWindowsDeploymentService
{
    /// <summary>
    /// Cleans and repartitions the target disk for UEFI Windows deployment.
    /// </summary>
    /// <param name="diskNumber">The disk number to partition.</param>
    /// <param name="workingDirectory">The directory used for temporary scripts.</param>
    /// <param name="cancellationToken">A token used to cancel diskpart execution.</param>
    /// <returns>The resulting target partition layout.</returns>
    Task<DeploymentTargetLayout> PrepareTargetDiskAsync(
        int diskNumber,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the WIM/ESD image index matching a requested edition.
    /// </summary>
    Task<int> ResolveImageIndexAsync(
        string imagePath,
        string requestedEdition,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a Windows image to the target Windows partition.
    /// </summary>
    Task ApplyImageAsync(
        string imagePath,
        int imageIndex,
        string windowsPartitionRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    /// <summary>
    /// Reads the currently applied Windows edition from the offline image.
    /// </summary>
    Task<string?> GetAppliedWindowsEditionAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes computer name and optional time zone into Windows\Panther\unattend.xml.
    /// </summary>
    Task ConfigureOfflineComputerNameAsync(
        string windowsPartitionRoot,
        string computerName,
        string processorArchitecture,
        string workingDirectory,
        string? defaultTimeZoneId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies and configures Windows RE on the recovery partition.
    /// </summary>
    Task ConfigureRecoveryEnvironmentAsync(
        string windowsPartitionRoot,
        string recoveryPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the recovery partition drive letter after recovery setup is complete.
    /// </summary>
    Task SealRecoveryPartitionAsync(
        string recoveryPartitionRoot,
        char recoveryPartitionLetter,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects INF drivers into the offline Windows image.
    /// </summary>
    Task ApplyOfflineDriversAsync(
        string windowsPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    /// <summary>
    /// Mounts WinRE, injects INF drivers, and unmounts the image with commit or discard semantics.
    /// </summary>
    Task ApplyRecoveryDriversAsync(
        string recoveryPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? mountProgress = null,
        IProgress<double>? applyProgress = null,
        IProgress<double>? unmountProgress = null,
        Action? onMountStarted = null,
        Action? onApplyStarted = null,
        Action? onUnmountStarted = null);

    /// <summary>
    /// Creates UEFI boot files for the applied Windows installation.
    /// </summary>
    Task ConfigureBootAsync(
        string windowsPartitionRoot,
        string systemPartitionRoot,
        int operatingSystemBuildMajor,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
