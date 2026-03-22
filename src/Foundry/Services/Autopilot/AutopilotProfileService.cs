using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Models.Configuration;
using Foundry.Services.Execution;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.Autopilot;

public sealed class AutopilotProfileService : IAutopilotProfileService
{
    private const string PowerShellExecutable = "powershell.exe";
    private const string ProfileFileName = "AutopilotConfigurationFile.json";

    private readonly IProcessExecutionService _processExecutionService;
    private readonly ILogger<AutopilotProfileService> _logger;

    public AutopilotProfileService(
        IProcessExecutionService processExecutionService,
        ILogger<AutopilotProfileService> logger)
    {
        _processExecutionService = processExecutionService;
        _logger = logger;
    }

    public async Task<AutopilotProfileSettings> ImportFromJsonFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = ValidateJsonContent(json, filePath);
        string displayName = ResolveDisplayName(document.RootElement, filePath);
        string id = BuildManualProfileId(json);

        return CreateProfileSettings(id, displayName, json, "Manual import", DateTimeOffset.UtcNow, preferredFolderName: null);
    }

    public async Task<IReadOnlyList<AutopilotProfileSettings>> DownloadFromTenantAsync(CancellationToken cancellationToken = default)
    {
        string workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "Foundry",
            "Autopilot",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);

        string scriptPath = Path.Combine(workingDirectory, "Export-FoundryAutopilotProfiles.ps1");
        string manifestPath = Path.Combine(workingDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            scriptPath,
            BuildDownloadScript(),
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);

        string arguments =
            $"-NoProfile -ExecutionPolicy Bypass -File {Quote(scriptPath)} " +
            $"-TargetDirectory {Quote(workingDirectory)} " +
            $"-ManifestPath {Quote(manifestPath)}";

        ProcessExecutionResult result = await _processExecutionService
            .RunAsync(PowerShellExecutable, arguments, workingDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogWarning("Autopilot tenant download failed. ExitCode={ExitCode}", result.ExitCode);
            throw new InvalidOperationException(result.ToDiagnosticText());
        }

        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("The Autopilot tenant download completed without a manifest file.");
        }

        await using FileStream manifestStream = File.OpenRead(manifestPath);
        ExportManifestItem[] manifestItems = await JsonSerializer.DeserializeAsync<ExportManifestItem[]>(
                manifestStream,
                Foundry.Services.Configuration.ConfigurationJsonDefaults.SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? [];

        List<AutopilotProfileSettings> profiles = [];
        foreach (ExportManifestItem item in manifestItems)
        {
            string profilePath = Path.Combine(workingDirectory, item.FolderName, ProfileFileName);
            if (!File.Exists(profilePath))
            {
                _logger.LogWarning(
                    "Skipping downloaded Autopilot profile because its JSON file is missing. ProfilePath={ProfilePath}",
                    profilePath);
                continue;
            }

            string json = await File.ReadAllTextAsync(profilePath, cancellationToken).ConfigureAwait(false);
            using JsonDocument _ = ValidateJsonContent(json, profilePath);

            profiles.Add(CreateProfileSettings(
                string.IsNullOrWhiteSpace(item.Id) ? BuildManualProfileId(json) : item.Id.Trim(),
                item.DisplayName,
                json,
                "Tenant download",
                DateTimeOffset.UtcNow,
                item.FolderName));
        }

        return profiles;
    }

    private static AutopilotProfileSettings CreateProfileSettings(
        string id,
        string displayName,
        string jsonContent,
        string source,
        DateTimeOffset importedAtUtc,
        string? preferredFolderName)
    {
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
            JsonContent = jsonContent,
            Source = source,
            ImportedAtUtc = importedAtUtc
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
                $"The Autopilot JSON file '{sourcePath}' contains non-ASCII characters. Use the Microsoft-exported Autopilot configuration format.");
        }

        return document;
    }

    private static string BuildManualProfileId(string jsonContent)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(jsonContent));
        return $"manual-{Convert.ToHexString(hash[..8]).ToLowerInvariant()}";
    }

    private static string BuildFolderName(string displayName, string id)
    {
        string sanitizedDisplayName = SanitizeFolderName(displayName);

        string safeId = new string(id.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
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

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal)
            ? $"\"{value}\""
            : value;
    }

    private static string BuildDownloadScript()
    {
        return """
param(
    [Parameter(Mandatory = $true)]
    [string] $TargetDirectory,

    [Parameter(Mandatory = $true)]
    [string] $ManifestPath
)

$ErrorActionPreference = "Stop"

function Assert-Command {
    param([string] $Name)

    if (-not (Get-Command -Name $Name -ErrorAction SilentlyContinue)) {
        throw "Required PowerShell command '$Name' was not found. Install the Microsoft Graph and Windows Autopilot profile prerequisites before downloading profiles."
    }
}

function Get-SafeFolderName {
    param(
        [string] $DisplayName,
        [string] $Id
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($DisplayName)) { "AutopilotProfile" } else { $DisplayName.Trim() }
    $invalidChars = [System.IO.Path]::GetInvalidFileNameChars()
    foreach ($character in $invalidChars) {
        $candidate = $candidate.Replace([string]$character, "_")
    }

    $candidate = $candidate.Replace(" ", "_")
    $safeId = if ([string]::IsNullOrWhiteSpace($Id)) { "" } else { ($Id -replace "[^a-zA-Z0-9_-]", "") }
    if ($safeId.Length -gt 12) {
        $safeId = $safeId.Substring(0, 12)
    }

    if ([string]::IsNullOrWhiteSpace($safeId)) {
        return $candidate
    }

    return "$candidate`__$safeId"
}

Assert-Command -Name "Connect-MgGraph"
Assert-Command -Name "Get-AutopilotProfile"
Assert-Command -Name "ConvertTo-AutopilotConfigurationJSON"

New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null

Connect-MgGraph -Scopes "Device.ReadWrite.All", "DeviceManagementManagedDevices.ReadWrite.All", "DeviceManagementServiceConfig.ReadWrite.All", "Domain.ReadWrite.All", "Group.ReadWrite.All", "GroupMember.ReadWrite.All", "User.Read" | Out-Null
$profiles = @(Get-AutopilotProfile)
$manifest = @()

foreach ($profile in $profiles) {
    $profileId = [string]$profile.id
    $displayName = [string]$profile.displayName
    $folderName = Get-SafeFolderName -DisplayName $displayName -Id $profileId
    $profileDirectory = Join-Path $TargetDirectory $folderName
    New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null

    $profile | ConvertTo-AutopilotConfigurationJSON | Set-Content -Encoding Ascii (Join-Path $profileDirectory "AutopilotConfigurationFile.json")

    $manifest += [PSCustomObject]@{
        id = $profileId
        displayName = $displayName
        folderName = $folderName
    }
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 $ManifestPath
""";
    }

    private sealed record ExportManifestItem
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string FolderName { get; init; } = string.Empty;
    }
}
