namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Offline paths for the staged interactive Autopilot registration assistant.
/// </summary>
public sealed record AutopilotInteractiveRegistrationProvisioningResult
{
    public required string RegistrationRootPath { get; init; }

    public required string ScriptPath { get; init; }

    public required string LauncherPath { get; init; }

    public required string OobeLauncherPath { get; init; }

    public required string OobeWaiterPath { get; init; }

    public required string ServiceUiPath { get; init; }

    public required string OobeCommandPath { get; init; }

    public required string ConfigPath { get; init; }

    public required string StateRootPath { get; init; }

    public required string LogRootPath { get; init; }
}
