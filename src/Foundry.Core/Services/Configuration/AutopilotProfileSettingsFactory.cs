using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class AutopilotProfileSettingsFactory
{
    public static AutopilotProfileSettings Create(
        string id,
        string displayName,
        string jsonContent,
        string source,
        DateTimeOffset importedAtUtc,
        string? preferredFolderName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        string normalizedId = string.IsNullOrWhiteSpace(id) ? BuildManualProfileId(jsonContent) : id.Trim();
        string normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? "Autopilot profile"
            : displayName.Trim();

        return new AutopilotProfileSettings
        {
            Id = normalizedId,
            DisplayName = normalizedDisplayName,
            FolderName = string.IsNullOrWhiteSpace(preferredFolderName)
                ? BuildFolderName(normalizedDisplayName, normalizedId)
                : SanitizeFolderName(preferredFolderName),
            Source = source,
            ImportedAtUtc = importedAtUtc,
            JsonContent = jsonContent
        };
    }

    public static string BuildManualProfileId(string jsonContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonContent);

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
