// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Deploy.Models;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Autopilot;

public sealed class AutopilotProfileCatalogService : IAutopilotProfileCatalogService
{
    public const string DefaultAutopilotRootPath = @"X:\Foundry\Config\Autopilot";
    private const string ConfigurationFileName = "AutopilotConfigurationFile.json";
    private const string EncryptedConfigurationFileName = "AutopilotConfigurationFile.json.encrypted";
    private const string CommentFilePropertyName = "Comment_File";

    private readonly IAutopilotProfileContentService _contentService;
    private readonly ILogger<AutopilotProfileCatalogService> _logger;
    private readonly string _autopilotRootPath;

    public AutopilotProfileCatalogService(
        IAutopilotProfileContentService contentService,
        ILogger<AutopilotProfileCatalogService> logger)
        : this(contentService, logger, DefaultAutopilotRootPath)
    {
    }

    internal AutopilotProfileCatalogService(
        IAutopilotProfileContentService contentService,
        ILogger<AutopilotProfileCatalogService> logger,
        string autopilotRootPath)
    {
        _contentService = contentService;
        _logger = logger;
        _autopilotRootPath = autopilotRootPath;
    }

    public IReadOnlyList<AutopilotProfileCatalogItem> LoadAvailableProfiles()
    {
        if (!Directory.Exists(_autopilotRootPath))
        {
            _logger.LogInformation(
                "Autopilot profile root was not found at '{AutopilotRootPath}'.",
                _autopilotRootPath);
            return [];
        }

        var profiles = new List<AutopilotProfileCatalogItem>();
        foreach (string directoryPath in Directory.EnumerateDirectories(_autopilotRootPath))
        {
            string folderName = Path.GetFileName(directoryPath);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            string encryptedConfigurationFilePath = Path.Combine(directoryPath, EncryptedConfigurationFileName);
            bool isProtected = File.Exists(encryptedConfigurationFilePath);
            string configurationFilePath = isProtected
                ? encryptedConfigurationFilePath
                : Path.Combine(directoryPath, ConfigurationFileName);
            if (!File.Exists(configurationFilePath))
            {
                _logger.LogDebug(
                    "Skipping Autopilot profile folder without configuration file. DirectoryPath={DirectoryPath}",
                    directoryPath);
                continue;
            }

            var profile = new AutopilotProfileCatalogItem
            {
                FolderName = folderName,
                DisplayName = folderName,
                ConfigurationFilePath = configurationFilePath,
                IsProtected = isProtected
            };
            profiles.Add(profile with { DisplayName = ResolveDisplayName(profile) });
        }

        AutopilotProfileCatalogItem[] ordered = profiles
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation(
            "Loaded {ProfileCount} Autopilot profile(s) from '{AutopilotRootPath}'.",
            ordered.Length,
            _autopilotRootPath);

        return ordered;
    }

    private string ResolveDisplayName(AutopilotProfileCatalogItem profile)
    {
        byte[]? content = null;
        try
        {
            content = _contentService.ReadAsync(profile).GetAwaiter().GetResult();
            using JsonDocument document = JsonDocument.Parse(content);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _logger.LogWarning(
                ex,
                "Failed to read Autopilot profile metadata from '{ConfigurationFilePath}'. Falling back to folder name.",
                profile.ConfigurationFilePath);
        }
        finally
        {
            if (content is not null)
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }

        return profile.FolderName;
    }
}
