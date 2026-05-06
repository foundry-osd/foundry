using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public sealed class AutopilotProfileImportService : IAutopilotProfileImportService
{
    private const string ProfileFileName = "AutopilotConfigurationFile.json";

    public async Task<AutopilotProfileSettings> ImportFromJsonFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ValidateJsonContent(jsonContent, filePath);
        string displayName = ResolveDisplayName(document.RootElement, filePath);
        string id = AutopilotProfileSettingsFactory.BuildManualProfileId(jsonContent);

        return AutopilotProfileSettingsFactory.Create(id, displayName, jsonContent, "Manual import", DateTimeOffset.UtcNow);
    }

    private static JsonDocument ValidateJsonContent(string jsonContent, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            throw new InvalidOperationException($"The Autopilot JSON file '{sourcePath}' is empty.");
        }

        JsonDocument document = JsonDocument.Parse(jsonContent);

        string roundTrip = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(jsonContent));
        if (!string.Equals(roundTrip, jsonContent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The Autopilot JSON file '{sourcePath}' contains non-ASCII characters.");
        }

        return document;
    }

    private static string ResolveDisplayName(JsonElement rootElement, string filePath)
    {
        if (rootElement.TryGetProperty("Comment_File", out JsonElement commentProperty) &&
            commentProperty.ValueKind == JsonValueKind.String)
        {
            string? comment = commentProperty.GetString();
            if (!string.IsNullOrWhiteSpace(comment))
            {
                return comment.Trim();
            }
        }

        string fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.Equals(ProfileFileName[..^5], StringComparison.OrdinalIgnoreCase))
        {
            string? parentDirectoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
            if (!string.IsNullOrWhiteSpace(parentDirectoryName))
            {
                return parentDirectoryName;
            }
        }

        return fileName;
    }

}
