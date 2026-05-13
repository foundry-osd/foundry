using Foundry.Core.Models.Configuration;
using Foundry.Telemetry;

namespace Foundry.Services.Configuration;

/// <summary>
/// Owns the mutable expert deployment configuration assembled by the Foundry UI.
/// </summary>
/// <remarks>
/// <see cref="Current"/> is always safe to persist. Volatile network secrets are kept outside the document and
/// merged only when <see cref="GenerateConnectProvisioningBundle"/> creates the Connect payload.
/// </remarks>
public interface IExpertDeployConfigurationStateService
{
    /// <summary>
    /// Occurs after the expert deployment configuration changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Gets the current expert configuration document after removing values that must not be persisted.
    /// </summary>
    FoundryExpertConfigurationDocument Current { get; }

    /// <summary>
    /// Gets a value indicating whether network configuration can be emitted for Connect.
    /// </summary>
    bool IsNetworkConfigurationReady { get; }

    /// <summary>
    /// Gets a value indicating whether Deploy configuration can be emitted.
    /// </summary>
    bool IsDeployConfigurationReady { get; }

    /// <summary>
    /// Gets a value indicating whether Connect provisioning files can be generated.
    /// </summary>
    bool IsConnectProvisioningReady { get; }

    /// <summary>
    /// Gets a value indicating whether required in-memory secrets are available.
    /// </summary>
    bool AreRequiredSecretsReady { get; }

    /// <summary>
    /// Gets a value indicating whether Autopilot staging is enabled.
    /// </summary>
    bool IsAutopilotEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether the selected Autopilot profiles are valid for output.
    /// </summary>
    bool IsAutopilotConfigurationReady { get; }

    /// <summary>
    /// Gets the selected Autopilot profile display name when a single profile is selected.
    /// </summary>
    string? SelectedAutopilotProfileDisplayName { get; }

    /// <summary>
    /// Gets the selected Autopilot profile folder name when a single profile is selected.
    /// </summary>
    string? SelectedAutopilotProfileFolderName { get; }

    /// <summary>
    /// Replaces the network configuration section and stores required secrets in volatile state.
    /// </summary>
    /// <param name="settings">New network settings.</param>
    void UpdateNetwork(NetworkSettings settings);

    /// <summary>
    /// Replaces the localization configuration section.
    /// </summary>
    /// <param name="settings">New localization settings.</param>
    void UpdateLocalization(LocalizationSettings settings);

    /// <summary>
    /// Replaces the customization configuration section.
    /// </summary>
    /// <param name="settings">New customization settings.</param>
    void UpdateCustomization(CustomizationSettings settings);

    /// <summary>
    /// Replaces the Autopilot configuration section.
    /// </summary>
    /// <param name="settings">New Autopilot settings.</param>
    void UpdateAutopilot(AutopilotSettings settings);

    /// <summary>
    /// Replaces telemetry settings propagated into generated runtime configuration.
    /// </summary>
    /// <param name="settings">New telemetry settings.</param>
    void UpdateTelemetry(TelemetrySettings settings);

    /// <summary>
    /// Generates Connect provisioning files after merging required volatile secrets back into the current network settings.
    /// </summary>
    /// <param name="stagingDirectoryPath">Directory where provisioning files should be staged.</param>
    /// <param name="telemetryOverride">Optional runtime telemetry settings used only for the generated Connect document.</param>
    /// <returns>The generated Connect provisioning bundle.</returns>
    FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(string stagingDirectoryPath, TelemetrySettings? telemetryOverride = null);

    /// <summary>
    /// Generates the Deploy configuration JSON for the current expert configuration.
    /// </summary>
    /// <param name="telemetryOverride">Optional runtime telemetry settings used only for the generated Deploy document.</param>
    /// <returns>Serialized Deploy configuration JSON.</returns>
    string GenerateDeployConfigurationJson(TelemetrySettings? telemetryOverride = null);
}
