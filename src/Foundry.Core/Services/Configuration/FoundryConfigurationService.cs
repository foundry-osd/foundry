using System.Text.Json;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public sealed class FoundryConfigurationService : IFoundryConfigurationService
{
    public string Serialize(FoundryConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
    }

    public FoundryConfigurationDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<FoundryConfigurationDocument>(json, ConfigurationJsonDefaults.SerializerOptions)
            ?? new FoundryConfigurationDocument();
    }
}
