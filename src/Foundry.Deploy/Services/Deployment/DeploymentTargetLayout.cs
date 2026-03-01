namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentTargetLayout
{
    public required int DiskNumber { get; init; }
    public required string SystemPartitionRoot { get; init; }
    public required string WindowsPartitionRoot { get; init; }
    public required string RecoveryPartitionRoot { get; init; }
    public required char RecoveryPartitionLetter { get; init; }
}
