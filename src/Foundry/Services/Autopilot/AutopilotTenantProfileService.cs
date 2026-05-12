using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Azure.Core;
using Azure.Identity;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Serilog;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Downloads Windows Autopilot deployment profiles from Microsoft Graph and converts them to offline profile JSON.
/// </summary>
public sealed class AutopilotTenantProfileService(ILogger logger) : IAutopilotTenantProfileService
{
    private const string DefaultClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
    private const string ClientIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_CLIENT_ID";
    private const string TenantIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_TENANT_ID";
    private const string DefaultTenantId = "common";
    private const string DefaultRedirectUri = "http://localhost";
    private const string OrganizationRequestPath = "v1.0/organization?$select=id,verifiedDomains";
    private const string AutopilotProfilesRequestPath = "beta/deviceManagement/windowsAutopilotDeploymentProfiles";
    private const string TenantDownloadSource = "Tenant download";

    private static readonly string[] GraphScopes =
    [
        "DeviceManagementServiceConfig.Read.All",
        "User.Read"
    ];

    private static readonly HttpClient GraphHttpClient = new()
    {
        BaseAddress = new Uri("https://graph.microsoft.com/", UriKind.Absolute)
    };

    private readonly ILogger logger = logger.ForContext<AutopilotTenantProfileService>();

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutopilotProfileSettings>> DownloadFromTenantAsync(CancellationToken cancellationToken = default)
    {
        TokenCredential credential = CreateCredential();

        logger.Information("Authenticating to Microsoft Graph for Autopilot profile download.");
        string accessToken = await AcquireAccessTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        OrganizationInfo organization = await GetOrganizationAsync(accessToken, cancellationToken).ConfigureAwait(false);
        logger.Information(
            "Authenticated to Microsoft Graph for Autopilot profile download. TenantId={TenantId}, TenantDomain={TenantDomain}",
            organization.Id,
            organization.DefaultDomain);

        IReadOnlyList<AutopilotDeploymentProfile> profiles = await GetAutopilotProfilesAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);

        AutopilotProfileSettings[] downloadedProfiles = profiles
            .Select(profile => CreateProfileSettings(profile, organization))
            .ToArray();

        logger.Information("Downloaded {ProfileCount} Autopilot profile(s) from Microsoft Graph.", downloadedProfiles.Length);
        return downloadedProfiles;
    }

    private static TokenCredential CreateCredential()
    {
        string clientId = Environment.GetEnvironmentVariable(ClientIdEnvironmentVariableName)?.Trim()
            ?? DefaultClientId;
        string? tenantId = Environment.GetEnvironmentVariable(TenantIdEnvironmentVariableName);

        return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            ClientId = clientId,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? DefaultTenantId : tenantId.Trim(),
            RedirectUri = new Uri(DefaultRedirectUri, UriKind.Absolute),
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                // Keep Graph auth reusable for Foundry without sharing a token cache name with unrelated tools.
                Name = "FoundryAutopilotGraph"
            }
        });
    }

    private static async Task<string> AcquireAccessTokenAsync(TokenCredential credential, CancellationToken cancellationToken)
    {
        AccessToken accessToken = await credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken)
            .ConfigureAwait(false);
        return accessToken.Token;
    }

    private static AutopilotProfileSettings CreateProfileSettings(
        AutopilotDeploymentProfile profile,
        OrganizationInfo organization)
    {
        string displayName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? "Autopilot profile"
            : profile.DisplayName.Trim();
        string jsonContent = BuildOfflineConfigurationJson(profile, organization, displayName);
        string id = string.IsNullOrWhiteSpace(profile.Id)
            ? AutopilotProfileSettingsFactory.BuildManualProfileId(jsonContent)
            : profile.Id.Trim();

        return AutopilotProfileSettingsFactory.Create(
            id,
            displayName,
            jsonContent,
            TenantDownloadSource,
            DateTimeOffset.UtcNow);
    }

    private static string BuildOfflineConfigurationJson(
        AutopilotDeploymentProfile profile,
        OrganizationInfo organization,
        string displayName)
    {
        OutOfBoxExperienceSettings oobeSettings = profile.OutOfBoxExperienceSettings ?? new();
        // Microsoft Graph has used both current and legacy property names for these OOBE flags.
        bool hideEscapeLink = oobeSettings.HideEscapeLink ?? oobeSettings.EscapeLinkHidden ?? false;
        bool hidePrivacySettings = oobeSettings.HidePrivacySettings ?? oobeSettings.PrivacySettingsHidden ?? false;
        bool hideEula = oobeSettings.HideEula ?? oobeSettings.EulaHidden ?? false;
        bool skipKeyboardSelectionPage = oobeSettings.SkipKeyboardSelectionPage ?? oobeSettings.KeyboardSelectionPageSkipped ?? false;
        int forcedEnrollment = hideEscapeLink ? 1 : 0;
        int oobeConfig = 8 + 256;

        if (string.Equals(oobeSettings.UserType, "standard", StringComparison.OrdinalIgnoreCase))
        {
            oobeConfig += 2;
        }

        if (hidePrivacySettings)
        {
            oobeConfig += 4;
        }

        if (hideEula)
        {
            oobeConfig += 16;
        }

        if (skipKeyboardSelectionPage)
        {
            oobeConfig += 1024;
        }

        if (string.Equals(oobeSettings.DeviceUsageType, "shared", StringComparison.OrdinalIgnoreCase))
        {
            oobeConfig += 96;
        }

        string aadServerData = JsonSerializer.Serialize(
            new CloudAssignedAadServerData(
                new ZeroTouchConfig(
                    organization.DefaultDomain,
                    string.Empty,
                    forcedEnrollment)),
            AutopilotGraphJsonSerializerContext.Default.CloudAssignedAadServerData);

        var configuration = new OfflineAutopilotConfiguration
        {
            CommentFile = $"Profile {displayName}",
            Version = 2049,
            ZtdCorrelationId = profile.Id ?? string.Empty,
            CloudAssignedDomainJoinMethod = string.Equals(
                profile.ODataType,
                "#microsoft.graph.activeDirectoryWindowsAutopilotDeploymentProfile",
                StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            CloudAssignedOobeConfig = oobeConfig,
            CloudAssignedForcedEnrollment = forcedEnrollment,
            CloudAssignedTenantId = organization.Id,
            CloudAssignedTenantDomain = organization.DefaultDomain,
            CloudAssignedAadServerData = aadServerData,
            CloudAssignedAutopilotUpdateDisabled = 1,
            CloudAssignedAutopilotUpdateTimeout = 1800000
        };

        if (!string.IsNullOrWhiteSpace(profile.DeviceNameTemplate))
        {
            configuration.CloudAssignedDeviceName = profile.DeviceNameTemplate;
        }

        if (skipKeyboardSelectionPage && !string.IsNullOrWhiteSpace(profile.Language))
        {
            configuration.CloudAssignedLanguage = profile.Language;
            configuration.CloudAssignedRegion = profile.Language;
        }

        if (profile.HybridAzureAdJoinSkipConnectivityCheck == true)
        {
            configuration.HybridJoinSkipDcConnectivityCheck = 1;
        }

        return JsonSerializer.Serialize(
            configuration,
            AutopilotGraphJsonSerializerContext.Default.OfflineAutopilotConfiguration);
    }

    private async Task<OrganizationInfo> GetOrganizationAsync(string accessToken, CancellationToken cancellationToken)
    {
        GraphCollectionResponse<OrganizationResponse>? response = await SendGraphRequestAsync(
            OrganizationRequestPath,
            accessToken,
            AutopilotGraphJsonSerializerContext.Default.GraphCollectionResponseOrganizationResponse,
            cancellationToken).ConfigureAwait(false);

        OrganizationResponse organization = response?.Value?.FirstOrDefault()
            ?? throw new InvalidOperationException("Microsoft Graph did not return organization information for the signed-in tenant.");

        string defaultDomain = organization.VerifiedDomains?
            .FirstOrDefault(domain => domain.IsDefault)?
            .Name
            ?? organization.VerifiedDomains?.FirstOrDefault()?.Name
            ?? throw new InvalidOperationException("Microsoft Graph did not return a verified domain for the signed-in tenant.");

        return new OrganizationInfo(
            organization.Id ?? throw new InvalidOperationException("Microsoft Graph did not return the tenant organization ID."),
            defaultDomain);
    }

    private async Task<IReadOnlyList<AutopilotDeploymentProfile>> GetAutopilotProfilesAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        var profiles = new List<AutopilotDeploymentProfile>();
        string? requestPath = AutopilotProfilesRequestPath;

        logger.Information("Requesting Autopilot deployment profiles from Microsoft Graph.");
        while (!string.IsNullOrWhiteSpace(requestPath))
        {
            GraphCollectionResponse<AutopilotDeploymentProfile>? response =
                await SendGraphRequestAsync(
                    requestPath,
                    accessToken,
                    AutopilotGraphJsonSerializerContext.Default.GraphCollectionResponseAutopilotDeploymentProfile,
                    cancellationToken).ConfigureAwait(false);

            if (response?.Value is not null)
            {
                profiles.AddRange(response.Value.Where(profile =>
                    !string.IsNullOrWhiteSpace(profile.Id) &&
                    !string.IsNullOrWhiteSpace(profile.DisplayName)));
            }

            // Graph pagination returns an absolute nextLink; HttpClient accepts it even with a configured base address.
            requestPath = response?.NextLink;
        }

        logger.Information("Retrieved {ProfileCount} Autopilot deployment profile record(s) from Microsoft Graph.", profiles.Count);
        return profiles;
    }

    private async Task<T?> SendGraphRequestAsync<T>(
        string requestPath,
        string accessToken,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await GraphHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string safeRequestName = GetSafeRequestName(requestPath);
            logger.Warning(
                "Microsoft Graph request failed. Request={Request}, StatusCode={StatusCode}, ReasonPhrase={ReasonPhrase}",
                safeRequestName,
                response.StatusCode,
                response.ReasonPhrase);
            throw new InvalidOperationException(
                $"Microsoft Graph request failed for '{safeRequestName}' with status code {(int)response.StatusCode}.");
        }

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync(
            responseStream,
            jsonTypeInfo,
            cancellationToken).ConfigureAwait(false);
    }

    private static string GetSafeRequestName(string requestPath)
    {
        return requestPath.Contains("windowsAutopilotDeploymentProfiles", StringComparison.OrdinalIgnoreCase)
            ? "Autopilot deployment profiles"
            : "Tenant organization";
    }
}

/// <summary>
/// Tenant identity information required to generate offline Autopilot JSON.
/// </summary>
/// <param name="Id">The Microsoft Entra tenant ID.</param>
/// <param name="DefaultDomain">The tenant default verified domain.</param>
internal sealed record OrganizationInfo(string Id, string DefaultDomain);

/// <summary>
/// Represents a Microsoft Graph collection response.
/// </summary>
/// <typeparam name="TItem">The collection item type.</typeparam>
internal sealed record GraphCollectionResponse<TItem>
{
    public List<TItem>? Value { get; init; }

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }
}

/// <summary>
/// Represents the subset of organization data required by offline Autopilot generation.
/// </summary>
internal sealed record OrganizationResponse
{
    public string? Id { get; init; }
    public List<VerifiedDomain>? VerifiedDomains { get; init; }
}

/// <summary>
/// Represents a verified tenant domain returned by Microsoft Graph.
/// </summary>
internal sealed record VerifiedDomain
{
    public string? Name { get; init; }
    public bool IsDefault { get; init; }
}

/// <summary>
/// Represents the Microsoft Graph Autopilot deployment profile payload consumed by Foundry.
/// </summary>
internal sealed record AutopilotDeploymentProfile
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

/// <summary>
/// Represents the OOBE-related flags returned on an Autopilot deployment profile.
/// </summary>
internal sealed record OutOfBoxExperienceSettings
{
    public string? UserType { get; init; }
    public string? DeviceUsageType { get; init; }
    public bool? HidePrivacySettings { get; init; }
    public bool? PrivacySettingsHidden { get; init; }

    [JsonPropertyName("hideEULA")]
    public bool? HideEula { get; init; }

    [JsonPropertyName("eulaHidden")]
    public bool? EulaHidden { get; init; }

    public bool? SkipKeyboardSelectionPage { get; init; }
    public bool? KeyboardSelectionPageSkipped { get; init; }
    public bool? HideEscapeLink { get; init; }
    public bool? EscapeLinkHidden { get; init; }
}

/// <summary>
/// Represents the nested Azure AD server data embedded in offline Autopilot JSON.
/// </summary>
/// <param name="ZeroTouchConfig">The zero-touch configuration payload.</param>
internal sealed record CloudAssignedAadServerData(ZeroTouchConfig ZeroTouchConfig);

/// <summary>
/// Represents the tenant zero-touch configuration embedded in offline Autopilot JSON.
/// </summary>
/// <param name="CloudAssignedTenantDomain">The tenant domain assigned to the device.</param>
/// <param name="CloudAssignedTenantUpn">The optional assigned user principal name.</param>
/// <param name="ForcedEnrollment">Whether enrollment is forced during OOBE.</param>
internal sealed record ZeroTouchConfig(
    string CloudAssignedTenantDomain,
    string CloudAssignedTenantUpn,
    int ForcedEnrollment);

/// <summary>
/// Represents the offline Autopilot configuration file staged for Windows OOBE.
/// </summary>
internal sealed record OfflineAutopilotConfiguration
{
    [JsonPropertyName("Comment_File")]
    public required string CommentFile { get; init; }

    public required int Version { get; init; }
    public required string ZtdCorrelationId { get; init; }
    public required int CloudAssignedDomainJoinMethod { get; init; }
    public required int CloudAssignedOobeConfig { get; init; }
    public required int CloudAssignedForcedEnrollment { get; init; }
    public required string CloudAssignedTenantId { get; init; }
    public required string CloudAssignedTenantDomain { get; init; }
    public required string CloudAssignedAadServerData { get; init; }
    public required int CloudAssignedAutopilotUpdateDisabled { get; init; }
    public required int CloudAssignedAutopilotUpdateTimeout { get; init; }
    public string? CloudAssignedDeviceName { get; set; }
    public string? CloudAssignedLanguage { get; set; }
    public string? CloudAssignedRegion { get; set; }

    [JsonPropertyName("HybridJoinSkipDCConnectivityCheck")]
    public int? HybridJoinSkipDcConnectivityCheck { get; set; }
}

/// <summary>
/// Provides source-generated JSON metadata for Microsoft Graph and offline Autopilot payloads.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphCollectionResponse<OrganizationResponse>))]
[JsonSerializable(typeof(GraphCollectionResponse<AutopilotDeploymentProfile>))]
[JsonSerializable(typeof(CloudAssignedAadServerData))]
[JsonSerializable(typeof(OfflineAutopilotConfiguration))]
internal sealed partial class AutopilotGraphJsonSerializerContext : JsonSerializerContext;
