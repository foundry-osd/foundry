using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public interface IConnectConfigurationGenerator
{
    FoundryConnectConfigurationDocument Generate(FoundryExpertConfigurationDocument document, string stagingDirectoryPath);

    FoundryConnectProvisioningBundle CreateProvisioningBundle(FoundryExpertConfigurationDocument document, string stagingDirectoryPath);

    string Serialize(FoundryConnectConfigurationDocument document);
}
