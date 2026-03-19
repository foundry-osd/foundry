using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentLaunchPreparationResult
{
    public required bool IsReadyToStart { get; init; }
    public required string StatusMessage { get; init; }
    public required string NormalizedComputerName { get; init; }
    public TargetDiskInfo? EffectiveTargetDisk { get; init; }
    public DeploymentContext? Context { get; init; }

    public static DeploymentLaunchPreparationResult Failure(string statusMessage, string normalizedComputerName)
    {
        return new DeploymentLaunchPreparationResult
        {
            IsReadyToStart = false,
            StatusMessage = statusMessage,
            NormalizedComputerName = normalizedComputerName
        };
    }

    public static DeploymentLaunchPreparationResult Success(
        string statusMessage,
        string normalizedComputerName,
        TargetDiskInfo effectiveTargetDisk,
        DeploymentContext context)
    {
        return new DeploymentLaunchPreparationResult
        {
            IsReadyToStart = true,
            StatusMessage = statusMessage,
            NormalizedComputerName = normalizedComputerName,
            EffectiveTargetDisk = effectiveTargetDisk,
            Context = context
        };
    }
}
