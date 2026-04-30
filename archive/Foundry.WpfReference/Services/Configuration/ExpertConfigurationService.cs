using System.Text.Json;
using Foundry.Models.Configuration;

namespace Foundry.Services.Configuration;

public sealed class ExpertConfigurationService : IExpertConfigurationService
{
    public async Task SaveAsync(string path, FoundryExpertConfigurationDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        string? directoryPath = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, ConfigurationJsonDefaults.SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<FoundryExpertConfigurationDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = File.OpenRead(path);
        FoundryExpertConfigurationDocument? document = await JsonSerializer.DeserializeAsync<FoundryExpertConfigurationDocument>(
                stream,
                ConfigurationJsonDefaults.SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false);

        return document ?? new FoundryExpertConfigurationDocument();
    }

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
