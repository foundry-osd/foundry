using Foundry.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface IExpertConfigurationService
{
    Task SaveAsync(string path, FoundryExpertConfigurationDocument document, CancellationToken cancellationToken = default);

    Task<FoundryExpertConfigurationDocument> LoadAsync(string path, CancellationToken cancellationToken = default);

    string Serialize(FoundryExpertConfigurationDocument document);

    FoundryExpertConfigurationDocument Deserialize(string json);
}
