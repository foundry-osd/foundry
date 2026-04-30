using Foundry.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface IFoundryConnectProvisioningService
{
    FoundryConnectProvisioningBundle Prepare(FoundryExpertConfigurationDocument document, string stagingDirectoryPath);
}
