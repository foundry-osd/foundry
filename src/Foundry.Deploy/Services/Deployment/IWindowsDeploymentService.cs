namespace Foundry.Deploy.Services.Deployment;

public interface IWindowsDeploymentService
{
    Task<DeploymentTargetLayout> PrepareTargetDiskAsync(
        int diskNumber,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task<int> ResolveImageIndexAsync(
        string imagePath,
        string requestedEdition,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task ApplyImageAsync(
        string imagePath,
        int imageIndex,
        string windowsPartitionRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

    Task<string?> GetAppliedWindowsEditionAsync(
        string windowsPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task ConfigureOfflineComputerNameAsync(
        string windowsPartitionRoot,
        string computerName,
        string processorArchitecture,
        string workingDirectory,
        string? defaultTimeZoneId = null,
        CancellationToken cancellationToken = default);

    Task ConfigureRecoveryEnvironmentAsync(
        string windowsPartitionRoot,
        string recoveryPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task SealRecoveryPartitionAsync(
        string recoveryPartitionRoot,
        char recoveryPartitionLetter,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task ApplyOfflineDriversAsync(
        string windowsPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        IProgress<double>? progress = null);

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

    Task ConfigureBootAsync(
        string windowsPartitionRoot,
        string systemPartitionRoot,
        int operatingSystemBuildMajor,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
