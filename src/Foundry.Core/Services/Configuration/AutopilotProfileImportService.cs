using System.Security.Cryptography;
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
        string id = BuildManualProfileId(jsonContent);

        return CreateProfileSettings(id, displayName, jsonContent, "Manual import", DateTimeOffset.UtcNow);
    }

    private static AutopilotProfileSettings CreateProfileSettings(
        string id,
        string displayName,
        string jsonContent,
        string source,
        DateTimeOffset importedAtUtc)
    {
        string normalizedId = string.IsNullOrWhiteSpace(id) ? BuildManualProfileId(jsonContent) : id.Trim();
        string normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? "Autopilot profile"
            : displayName.Trim();

        return new AutopilotProfileSettings
        {
            Id = normalizedId,
            DisplayName = normalizedDisplayName,
            FolderName = BuildFolderName(normalizedDisplayName, normalizedId),
            Source = source,
            ImportedAtUtc = importedAtUtc,
            JsonContent = jsonContent
        };
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

    private static string BuildManualProfileId(string jsonContent)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(jsonContent));
        return $"manual-{Convert.ToHexString(hash[..8]).ToLowerInvariant()}";
    }

    private static string BuildFolderName(string displayName, string id)
    {
        string sanitizedDisplayName = SanitizeFolderName(displayName);
        string safeId = new(id.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        if (safeId.Length > 8)
        {
            safeId = safeId[..8];
        }

        return string.IsNullOrWhiteSpace(safeId)
            ? sanitizedDisplayName
            : $"{sanitizedDisplayName}__{safeId}";
    }

    private static string SanitizeFolderName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new string(value
                .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
                .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "AutopilotProfile";
        }

        return sanitized.Replace(' ', '_');
    }
}
