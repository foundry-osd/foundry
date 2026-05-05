using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface IExpertDeployConfigurationStateService
{
    event EventHandler? StateChanged;

    FoundryExpertConfigurationDocument Current { get; }

    bool IsDeployConfigurationReady { get; }

    void UpdateLocalization(LocalizationSettings settings);

    string GenerateDeployConfigurationJson();
}
