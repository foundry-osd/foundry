using System.Text.Json;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public sealed class ExpertConfigurationService : IExpertConfigurationService
{
    public string Serialize(FoundryExpertConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
    }

    public FoundryExpertConfigurationDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<FoundryExpertConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)
            ?? new FoundryExpertConfigurationDocument();
    }
}
