using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.Configuration;

public interface IDeployConfigurationGenerator
{
    FoundryDeployConfigurationDocument Generate(FoundryExpertConfigurationDocument document);

    string Serialize(FoundryDeployConfigurationDocument document);
}
