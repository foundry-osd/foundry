using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface IExpertDeployConfigurationStateService
{
    event EventHandler? StateChanged;

    FoundryExpertConfigurationDocument Current { get; }

    bool IsDeployConfigurationReady { get; }

    void UpdateNetwork(NetworkSettings settings);

    void UpdateLocalization(LocalizationSettings settings);

    FoundryConnectProvisioningBundle GenerateConnectProvisioningBundle(string stagingDirectoryPath);

    string GenerateDeployConfigurationJson();
}
