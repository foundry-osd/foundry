// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Azure.Core;
using Azure.Identity;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Autopilot;
using Serilog;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Downloads Windows Autopilot deployment profiles from Microsoft Graph and converts them to offline profile JSON.
/// </summary>
public sealed class AutopilotTenantProfileService(ILogger logger) : IAutopilotTenantProfileService
{
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
    public async Task<AutopilotProfileDownloadResult> DownloadFromTenantAsync(CancellationToken cancellationToken = default)
    {
        TokenCredential credential = CreateCredential();

        logger.Information("Authenticating to Microsoft Graph for Autopilot profile download.");
        string accessToken = await AcquireAccessTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        AutopilotTenantIdentity organization = await GetOrganizationAsync(accessToken, cancellationToken).ConfigureAwait(false);
        logger.Information(
            "Authenticated to Microsoft Graph for Autopilot profile download. TenantResolved={TenantResolved}, DomainResolved={DomainResolved}",
            organization.Id != Guid.Empty,
            !string.IsNullOrWhiteSpace(organization.DefaultDomain));

        IReadOnlyList<AutopilotDeploymentProfile> profiles = await GetAutopilotProfilesAsync(accessToken, cancellationToken)
            .ConfigureAwait(false);

        var supported = new List<AutopilotProfileSettings>();
        var rejected = new List<AutopilotProfileRejection>();
        foreach (AutopilotDeploymentProfile profile in profiles)
        {
            try { supported.Add(CreateProfileSettings(profile, organization)); }
            catch (InvalidDataException error)
            {
                rejected.Add(new(profile.DisplayName ?? "Autopilot profile", error.Message));
            }
        }
        logger.Information("Downloaded Autopilot profiles. SupportedCount={SupportedCount}, RejectedCount={RejectedCount}.", supported.Count, rejected.Count);
        return new(supported, rejected);
    }

    private static TokenCredential CreateCredential()
    {
        string clientId = Environment.GetEnvironmentVariable(AutopilotGraphAuthenticationDefaults.ClientIdEnvironmentVariableName)?.Trim()
            ?? AutopilotGraphAuthenticationDefaults.FoundryBootstrapClientId;
        string? tenantId = Environment.GetEnvironmentVariable(AutopilotGraphAuthenticationDefaults.TenantIdEnvironmentVariableName);

        return new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
        {
            ClientId = clientId,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? AutopilotGraphAuthenticationDefaults.DefaultTenantId : tenantId.Trim(),
            RedirectUri = new Uri(AutopilotGraphAuthenticationDefaults.RedirectUri, UriKind.Absolute)
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
        AutopilotTenantIdentity organization)
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
        AutopilotDeploymentProfile profile, AutopilotTenantIdentity organization, string displayName)
    {
        OutOfBoxExperienceSettings settings = profile.OutOfBoxExperienceSettings ?? new();
        if (!Guid.TryParseExact(profile.Id, "D", out Guid id) || id == Guid.Empty)
            throw new InvalidDataException("The Autopilot profile ID is invalid.");
        AutopilotOfflineJoinType join = profile.ODataType?.ToLowerInvariant() switch
        {
            "#microsoft.graph.azureadwindowsautopilotdeploymentprofile" => AutopilotOfflineJoinType.Entra,
            "#microsoft.graph.activedirectorywindowsautopilotdeploymentprofile" => AutopilotOfflineJoinType.Hybrid,
            _ => throw new InvalidDataException("The Autopilot profile join type is unsupported for offline JSON.")
        };
        AutopilotOfflineDeploymentMode mode = settings.DeviceUsageType?.ToLowerInvariant() switch
        {
            null or "singleuser" => AutopilotOfflineDeploymentMode.UserDriven,
            "shared" => AutopilotOfflineDeploymentMode.SelfDeploying,
            _ => throw new InvalidDataException("The Autopilot device usage mode is unsupported for offline JSON.")
        };
        AutopilotOfflineUserType user = settings.UserType?.ToLowerInvariant() switch
        {
            null or "administrator" => AutopilotOfflineUserType.Administrator,
            "standard" => AutopilotOfflineUserType.Standard,
            _ => throw new InvalidDataException("The Autopilot user type is unsupported for offline JSON.")
        };
        return AutopilotOfflineProfileConverter.Convert(new()
        {
            Id = id,
            DisplayName = displayName,
            JoinType = join,
            Mode = mode,
            UserType = user,
            PreprovisioningAllowed = profile.PreprovisioningAllowed ?? false,
            DeviceNameTemplate = profile.DeviceNameTemplate,
            Language = profile.Language,
            HideEscapeLink = ResolveFlag(settings.HideEscapeLink, settings.EscapeLinkHidden),
            HidePrivacySettings = ResolveFlag(settings.HidePrivacySettings, settings.PrivacySettingsHidden),
            HideEula = ResolveFlag(settings.HideEula, settings.EulaHidden),
            SkipKeyboardSelectionPage = ResolveFlag(settings.SkipKeyboardSelectionPage, settings.KeyboardSelectionPageSkipped),
            HybridJoinSkipConnectivityCheck = profile.HybridAzureAdJoinSkipConnectivityCheck ?? false
        }, organization);
    }

    private static bool ResolveFlag(bool? current, bool? legacy)
    {
        if (current.HasValue && legacy.HasValue && current != legacy)
            throw new InvalidDataException("The Autopilot profile contains conflicting current and legacy OOBE settings.");
        return current ?? legacy ?? false;
    }

    private async Task<AutopilotTenantIdentity> GetOrganizationAsync(string accessToken, CancellationToken cancellationToken)
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

        if (!Guid.TryParseExact(organization.Id, "D", out Guid tenantId) || tenantId == Guid.Empty)
            throw new InvalidDataException("Microsoft Graph did not return a valid tenant organization ID.");
        return new AutopilotTenantIdentity(tenantId, defaultDomain);
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
    public bool? PreprovisioningAllowed { get; init; }
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
/// Provides source-generated JSON metadata for Microsoft Graph and offline Autopilot payloads.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GraphCollectionResponse<OrganizationResponse>))]
[JsonSerializable(typeof(GraphCollectionResponse<AutopilotDeploymentProfile>))]
internal sealed partial class AutopilotGraphJsonSerializerContext : JsonSerializerContext;
