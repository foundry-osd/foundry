namespace Foundry.Deploy.Services.Wizard;

public sealed record DeploymentWizardStateSnapshot
{
    public required int WizardStepIndex { get; init; }
    public required bool IsDeploymentRunning { get; init; }
    public required bool IsCatalogLoading { get; init; }
    public required bool IsTargetDiskLoading { get; init; }
    public required bool IsDebugSafeMode { get; init; }
    public required bool IsTargetComputerNameValid { get; init; }
    public required bool HasSelectedOperatingSystem { get; init; }
    public required bool HasTargetDiskSelection { get; init; }
    public required bool IsSelectedTargetDiskSelectable { get; init; }
    public required bool HasValidDriverPackSelection { get; init; }
    public required bool HasValidAutopilotSelection { get; init; }
    public required bool IsOperatingSystemCatalogReadyForNavigation { get; init; }
}
