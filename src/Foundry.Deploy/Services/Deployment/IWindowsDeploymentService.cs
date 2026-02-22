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
        CancellationToken cancellationToken = default);

    Task ApplyOfflineDriversAsync(
        string windowsPartitionRoot,
        string driverRoot,
        string scratchDirectory,
        string workingDirectory,
        CancellationToken cancellationToken = default);

    Task ConfigureBootAsync(
        string windowsPartitionRoot,
        string systemPartitionRoot,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
