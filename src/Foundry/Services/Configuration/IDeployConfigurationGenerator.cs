using Foundry.Models.Configuration;
using Foundry.Models.Configuration.Deploy;

namespace Foundry.Services.Configuration;

public interface IDeployConfigurationGenerator
{
    FoundryDeployConfigurationDocument Generate(FoundryExpertConfigurationDocument document);

    string Serialize(FoundryDeployConfigurationDocument document);
}
