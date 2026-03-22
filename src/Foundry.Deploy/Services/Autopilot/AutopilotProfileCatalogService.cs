using System.IO;
using System.Text.Json;
using Foundry.Deploy.Models;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

public sealed class AutopilotProfileCatalogService(
    ILogger<AutopilotProfileCatalogService> logger) : IAutopilotProfileCatalogService
{
    public const string DefaultAutopilotRootPath = @"X:\Foundry\Config\Autopilot";
    private const string ConfigurationFileName = "AutopilotConfigurationFile.json";
    private const string CommentFilePropertyName = "Comment_File";

    private readonly ILogger<AutopilotProfileCatalogService> _logger = logger;

    public IReadOnlyList<AutopilotProfileCatalogItem> LoadAvailableProfiles()
    {
        if (!Directory.Exists(DefaultAutopilotRootPath))
        {
            _logger.LogInformation(
                "Autopilot profile root was not found at '{AutopilotRootPath}'.",
                DefaultAutopilotRootPath);
            return [];
        }

        var profiles = new List<AutopilotProfileCatalogItem>();
        foreach (string directoryPath in Directory.EnumerateDirectories(DefaultAutopilotRootPath))
        {
            string folderName = Path.GetFileName(directoryPath);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            string configurationFilePath = Path.Combine(directoryPath, ConfigurationFileName);
            if (!File.Exists(configurationFilePath))
            {
                _logger.LogDebug(
                    "Skipping Autopilot profile folder without configuration file. DirectoryPath={DirectoryPath}",
                    directoryPath);
                continue;
            }

            profiles.Add(new AutopilotProfileCatalogItem
            {
                FolderName = folderName,
                DisplayName = ResolveDisplayName(configurationFilePath, folderName),
                ConfigurationFilePath = configurationFilePath
            });
        }

        AutopilotProfileCatalogItem[] ordered = profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation(
            "Loaded {ProfileCount} Autopilot profile(s) from '{AutopilotRootPath}'.",
            ordered.Length,
            DefaultAutopilotRootPath);

        return ordered;
    }

    private string ResolveDisplayName(string configurationFilePath, string fallbackFolderName)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configurationFilePath));
            if (document.RootElement.TryGetProperty(CommentFilePropertyName, out JsonElement commentElement) &&
                commentElement.ValueKind == JsonValueKind.String)
            {
                string? displayName = commentElement.GetString();
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName.Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(
                ex,
                "Failed to read Autopilot profile metadata from '{ConfigurationFilePath}'. Falling back to folder name.",
                configurationFilePath);
        }

        return fallbackFolderName;
    }
}
