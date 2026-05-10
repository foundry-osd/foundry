using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface IExpertDeployConfigurationStateService
{
    event EventHandler? StateChanged;

    FoundryExpertConfigurationDocument Current { get; }

    bool IsNetworkConfigurationReady { get; }

    bool IsDeployConfigurationReady { get; }

    bool IsConnectProvisioningReady { get; }

    bool AreRequiredSecretsReady { get; }

    bool IsAutopilotEnabled { get; }

    bool IsAutopilotConfigurationReady { get; }

    string? SelectedAutopilotProfileDisplayName { get; }

    string? SelectedAutopilotProfileFolderName { get; }

    void UpdateNetwork(NetworkSettings settings);

    void UpdateLocalization(LocalizationSettings settings);

    void UpdateCustomization(CustomizationSettings settings);

    void UpdateAutopilot(AutopilotSettings settings);

    FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(string stagingDirectoryPath);

    string GenerateDeployConfigurationJson();
}
