using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Foundry.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.Autopilot;

public sealed class AutopilotProfileService : IAutopilotProfileService
{
    private const string ClientIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_CLIENT_ID";
    private const string TenantIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_TENANT_ID";
    private const string DefaultTenantId = "common";
    private const string DefaultRedirectUri = "http://localhost";
    private const string ProfileFileName = "AutopilotConfigurationFile.json";
    private const string OrganizationRequestPath = "v1.0/organization?$select=id,verifiedDomains";
    private const string AutopilotProfilesRequestPath = "beta/deviceManagement/windowsAutopilotDeploymentProfiles";

    private static readonly string[] GraphScopes =
    [
        "DeviceManagementServiceConfig.Read.All",
        "User.Read"
    ];

    private static readonly HttpClient GraphHttpClient = new()
    {
        BaseAddress = new Uri("https://graph.microsoft.com/", UriKind.Absolute)
    };

    private readonly ILogger<AutopilotProfileService> _logger;

    public AutopilotProfileService(ILogger<AutopilotProfileService> logger)
    {
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
        TokenCredential credential = CreateCredential();
        string accessToken = await AcquireAccessTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        OrganizationInfo organization = await GetOrganizationAsync(accessToken, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AutopilotDeploymentProfile> profiles = await GetAutopilotProfilesAsync(accessToken, cancellationToken).ConfigureAwait(false);

        var downloadedProfiles = new List<AutopilotProfileSettings>(profiles.Count);
        foreach (AutopilotDeploymentProfile profile in profiles)
        {
            string displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                ? "Autopilot profile"
                : profile.DisplayName.Trim();
            string json = BuildOfflineConfigurationJson(profile, organization);
            downloadedProfiles.Add(CreateProfileSettings(
                string.IsNullOrWhiteSpace(profile.Id) ? BuildManualProfileId(json) : profile.Id.Trim(),
                displayName,
                json,
                "Tenant download",
                DateTimeOffset.UtcNow,
                preferredFolderName: null));
        }

        _logger.LogInformation("Downloaded {ProfileCount} Autopilot profile(s) from Microsoft Graph.", downloadedProfiles.Count);
        return downloadedProfiles;
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

    private static TokenCredential CreateCredential()
    {
        string? clientId = Environment.GetEnvironmentVariable(ClientIdEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                $"Set the environment variable '{ClientIdEnvironmentVariableName}' to the client ID of a Microsoft Entra public client app registration that has delegated Microsoft Graph access.");
        }

        string? tenantId = Environment.GetEnvironmentVariable(TenantIdEnvironmentVariableName);
        return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            ClientId = clientId.Trim(),
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? DefaultTenantId : tenantId.Trim(),
            RedirectUri = new Uri(DefaultRedirectUri, UriKind.Absolute),
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "FoundryAutopilotGraph"
            }
        });
    }

    private static async Task<string> AcquireAccessTokenAsync(TokenCredential credential, CancellationToken cancellationToken)
    {
        AccessToken accessToken = await credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken).ConfigureAwait(false);
        return accessToken.Token;
    }

    private static string BuildOfflineConfigurationJson(AutopilotDeploymentProfile profile, OrganizationInfo organization)
    {
        OutOfBoxExperienceSettings oobeSettings = profile.OutOfBoxExperienceSettings ?? new OutOfBoxExperienceSettings();
        int forcedEnrollment = oobeSettings.HideEscapeLink == true ? 1 : 0;
        int oobeConfig = 8 + 256;

        if (string.Equals(oobeSettings.UserType, "standard", StringComparison.OrdinalIgnoreCase))
        {
            oobeConfig += 2;
        }

        if (oobeSettings.HidePrivacySettings == true)
        {
            oobeConfig += 4;
        }

        if (oobeSettings.HideEula == true)
        {
            oobeConfig += 16;
        }

        if (oobeSettings.SkipKeyboardSelectionPage == true)
        {
            oobeConfig += 1024;
        }

        if (string.Equals(oobeSettings.DeviceUsageType, "shared", StringComparison.OrdinalIgnoreCase))
        {
            oobeConfig += 96;
        }

        var json = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Comment_File"] = $"Profile {profile.DisplayName}",
            ["Version"] = 2049,
            ["ZtdCorrelationId"] = profile.Id ?? string.Empty,
            ["CloudAssignedDomainJoinMethod"] = string.Equals(
                profile.ODataType,
                "#microsoft.graph.activeDirectoryWindowsAutopilotDeploymentProfile",
                StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            ["CloudAssignedOobeConfig"] = oobeConfig,
            ["CloudAssignedForcedEnrollment"] = forcedEnrollment,
            ["CloudAssignedTenantId"] = organization.Id,
            ["CloudAssignedTenantDomain"] = organization.DefaultDomain,
            ["CloudAssignedAadServerData"] = JsonSerializer.Serialize(new
            {
                ZeroTouchConfig = new
                {
                    CloudAssignedTenantDomain = organization.DefaultDomain,
                    CloudAssignedTenantUpn = string.Empty,
                    ForcedEnrollment = forcedEnrollment
                }
            }),
            ["CloudAssignedAutopilotUpdateDisabled"] = 1,
            ["CloudAssignedAutopilotUpdateTimeout"] = 1800000
        };

        if (!string.IsNullOrWhiteSpace(profile.DeviceNameTemplate))
        {
            json["CloudAssignedDeviceName"] = profile.DeviceNameTemplate;
        }

        if (oobeSettings.SkipKeyboardSelectionPage == true && !string.IsNullOrWhiteSpace(profile.Language))
        {
            json["CloudAssignedLanguage"] = profile.Language;
            json["CloudAssignedRegion"] = profile.Language;
        }

        if (profile.HybridAzureAdJoinSkipConnectivityCheck == true)
        {
            json["HybridJoinSkipDCConnectivityCheck"] = 1;
        }

        return JsonSerializer.Serialize(json, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private async Task<OrganizationInfo> GetOrganizationAsync(string accessToken, CancellationToken cancellationToken)
    {
        GraphCollectionResponse<OrganizationResponse>? response = await SendGraphRequestAsync<GraphCollectionResponse<OrganizationResponse>>(
            OrganizationRequestPath,
            accessToken,
            cancellationToken).ConfigureAwait(false);

        OrganizationResponse organization = response?.Value?.FirstOrDefault()
            ?? throw new InvalidOperationException("Microsoft Graph did not return organization information for the signed-in tenant.");

        string defaultDomain = organization.VerifiedDomains?
            .FirstOrDefault(domain => domain.IsDefault)?
            .Name
            ?? organization.VerifiedDomains?.FirstOrDefault()?.Name
            ?? throw new InvalidOperationException("Microsoft Graph did not return a verified domain for the signed-in tenant.");

        return new OrganizationInfo
        {
            Id = organization.Id ?? throw new InvalidOperationException("Microsoft Graph did not return the tenant organization ID."),
            DefaultDomain = defaultDomain
        };
    }

    private async Task<IReadOnlyList<AutopilotDeploymentProfile>> GetAutopilotProfilesAsync(string accessToken, CancellationToken cancellationToken)
    {
        var profiles = new List<AutopilotDeploymentProfile>();
        string? requestPath = AutopilotProfilesRequestPath;

        while (!string.IsNullOrWhiteSpace(requestPath))
        {
            GraphCollectionResponse<AutopilotDeploymentProfile>? response =
                await SendGraphRequestAsync<GraphCollectionResponse<AutopilotDeploymentProfile>>(
                    requestPath,
                    accessToken,
                    cancellationToken).ConfigureAwait(false);

            if (response?.Value is not null)
            {
                profiles.AddRange(response.Value.Where(profile =>
                    !string.IsNullOrWhiteSpace(profile.Id) &&
                    !string.IsNullOrWhiteSpace(profile.DisplayName)));
            }

            requestPath = response?.NextLink;
        }

        return profiles;
    }

    private async Task<T?> SendGraphRequestAsync<T>(string requestPath, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await GraphHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Microsoft Graph request failed. RequestPath={RequestPath}, StatusCode={StatusCode}",
                requestPath,
                response.StatusCode);
            throw new InvalidOperationException(
                $"Microsoft Graph request failed for '{requestPath}' with status code {(int)response.StatusCode}: {responseBody}");
        }

        return JsonSerializer.Deserialize<T>(responseBody);
    }

    private sealed record OrganizationInfo
    {
        public required string Id { get; init; }
        public required string DefaultDomain { get; init; }
    }

    private sealed record GraphCollectionResponse<TItem>
    {
        public List<TItem>? Value { get; init; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }
    }

    private sealed record OrganizationResponse
    {
        public string? Id { get; init; }
        public List<VerifiedDomain>? VerifiedDomains { get; init; }
    }

    private sealed record VerifiedDomain
    {
        public string? Name { get; init; }
        public bool IsDefault { get; init; }
    }

    private sealed record AutopilotDeploymentProfile
    {
        [JsonPropertyName("@odata.type")]
        public string? ODataType { get; init; }

        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public string? DeviceNameTemplate { get; init; }
        public string? Language { get; init; }

        [JsonPropertyName("hybridAzureADJoinSkipConnectivityCheck")]
        public bool? HybridAzureAdJoinSkipConnectivityCheck { get; init; }

        public OutOfBoxExperienceSettings? OutOfBoxExperienceSettings { get; init; }
    }

    private sealed record OutOfBoxExperienceSettings
    {
        public string? UserType { get; init; }
        public bool? HidePrivacySettings { get; init; }

        [JsonPropertyName("hideEULA")]
        public bool? HideEula { get; init; }

        public bool? SkipKeyboardSelectionPage { get; init; }
        public string? DeviceUsageType { get; init; }
        public bool? HideEscapeLink { get; init; }
    }
}
