using Foundry.Connect.Models.Configuration;

namespace Foundry.Connect.Services.Configuration;

public interface IConnectConfigurationService
{
    string? ConfigurationPath { get; }

    bool IsLoadedFromDisk { get; }

    FoundryConnectConfiguration Load();
}
