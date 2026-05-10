using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public interface IExpertConfigurationService
{
    string Serialize(FoundryExpertConfigurationDocument document);

    FoundryExpertConfigurationDocument Deserialize(string json);
}
