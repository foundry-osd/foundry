using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Serilog;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Performs interactive Microsoft Graph tenant onboarding for Autopilot hardware hash upload.
/// </summary>
public sealed class AutopilotTenantOnboardingService(ILogger logger) : IAutopilotTenantOnboardingService
{
    private const string DefaultClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
    private const string ClientIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_CLIENT_ID";
    private const string TenantIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_TENANT_ID";
    private const string DefaultTenantId = "common";
    private const string DefaultRedirectUri = "http://localhost";
    private const string GraphAppId = "00000003-0000-0000-c000-000000000000";
    private const string OrganizationRequestPath = "v1.0/organization?$select=id";
    private const string ApplicationSelect = "$select=id,appId,displayName,requiredResourceAccess,keyCredentials";
    private const string ServicePrincipalSelect = "$select=id,appId,accountEnabled";
    private const string GroupTagRequestPath = "v1.0/deviceManagement/windowsAutopilotDeviceIdentities?$select=groupTag";

    private static readonly string[] GraphScopes =
    [
        "Application.ReadWrite.All",
        "AppRoleAssignment.ReadWrite.All",
        "DeviceManagementServiceConfig.Read.All"
    ];

    private static readonly HttpClient GraphHttpClient = new()
    {
        BaseAddress = new Uri("https://graph.microsoft.com/", UriKind.Absolute)
    };

    private readonly ILogger logger = logger.ForContext<AutopilotTenantOnboardingService>();

    /// <inheritdoc />
    public async Task<AutopilotTenantOnboardingResult> ConnectAsync(
        AutopilotHardwareHashUploadSettings currentSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);

        TokenCredential credential = CreateCredential();
        string accessToken = await AcquireAccessTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        string tenantId = await GetTenantIdAsync(accessToken, cancellationToken).ConfigureAwait(false);
        GraphAppRole requiredRole = await GetGraphAppRoleAsync(
            accessToken,
            AutopilotGraphPermissionCatalog.DeviceManagementServiceConfigReadWriteAll,
            cancellationToken).ConfigureAwait(false);

        AutopilotGraphApplication? application = await FindApplicationAsync(
            accessToken,
            currentSettings.Tenant.ApplicationObjectId,
            requiredRole,
            cancellationToken).ConfigureAwait(false);
        application ??= await FindApplicationByDisplayNameAsync(accessToken, requiredRole, cancellationToken).ConfigureAwait(false);

        if (application is null)
        {
            application = await CreateApplicationAsync(accessToken, requiredRole, cancellationToken).ConfigureAwait(false);
            logger.Information("Created managed Autopilot app registration. ApplicationObjectId={ApplicationObjectId}", application.ObjectId);
        }
        else if (string.IsNullOrWhiteSpace(currentSettings.Tenant.ApplicationObjectId) &&
                 string.Equals(application.DisplayName, AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            logger.Information("Adopted existing managed Autopilot app registration. ApplicationObjectId={ApplicationObjectId}", application.ObjectId);
        }

        application = await EnsureRequiredApplicationPermissionAsync(
            accessToken,
            application,
            requiredRole,
            cancellationToken).ConfigureAwait(false);

        AutopilotGraphServicePrincipal? servicePrincipal = await FindServicePrincipalAsync(
            accessToken,
            application.ClientId,
            requiredRole,
            cancellationToken).ConfigureAwait(false);
        servicePrincipal ??= await CreateServicePrincipalAsync(
            accessToken,
            application.ClientId,
            requiredRole,
            cancellationToken).ConfigureAwait(false);

        servicePrincipal = await EnsureAdminConsentAsync(
            accessToken,
            servicePrincipal,
            requiredRole,
            cancellationToken).ConfigureAwait(false);

        string[] groupTags = await GetGroupTagsAsync(accessToken, cancellationToken).ConfigureAwait(false);
        AutopilotTenantOnboardingSnapshot snapshot = new()
        {
            TenantId = tenantId,
            PersistedApplicationObjectId = application.ObjectId,
            ManagedAppDisplayName = AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName,
            Applications = [application],
            ServicePrincipal = servicePrincipal,
            ActiveCertificate = currentSettings.ActiveCertificate,
            KeyCredentials = await GetApplicationKeyCredentialsAsync(accessToken, application.ObjectId, cancellationToken).ConfigureAwait(false),
            CurrentTimeUtc = DateTimeOffset.UtcNow
        };
        AutopilotTenantOnboardingEvaluation evaluation = AutopilotTenantOnboardingEvaluator.Evaluate(snapshot);
        AutopilotHardwareHashUploadSettings updatedSettings = currentSettings with
        {
            Tenant = new AutopilotTenantRegistrationSettings
            {
                TenantId = tenantId,
                ApplicationObjectId = application.ObjectId,
                ClientId = application.ClientId,
                ServicePrincipalObjectId = servicePrincipal.ObjectId
            },
            KnownGroupTags = groupTags,
            DefaultGroupTag = ResolveDefaultGroupTag(currentSettings.DefaultGroupTag, groupTags)
        };

        return new AutopilotTenantOnboardingResult
        {
            Settings = updatedSettings,
            Status = evaluation.Status,
            Message = evaluation.Status == AutopilotTenantOnboardingStatus.Ready
                ? "The managed app registration is ready."
                : $"The managed app registration requires attention: {evaluation.Status}."
        };
    }

    /// <inheritdoc />
    public async Task<AutopilotCertificateCreationResult> CreateCertificateAsync(
        AutopilotHardwareHashUploadSettings currentSettings,
        string pfxOutputPath,
        int validityMonths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(pfxOutputPath);

        if (string.IsNullOrWhiteSpace(currentSettings.Tenant.ApplicationObjectId))
        {
            throw new InvalidOperationException("The managed app registration must be connected before creating a certificate.");
        }

        TokenCredential credential = CreateCredential();
        string accessToken = await AcquireAccessTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        DateTimeOffset startsOnUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        DateTimeOffset expiresOnUtc = DateTimeOffset.UtcNow.AddMonths(validityMonths);
        string keyId = Guid.NewGuid().ToString("D");
        string password = GeneratePfxPassword();

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(startsOnUtc, expiresOnUtc);
        byte[] pfxBytes = certificate.Export(X509ContentType.Pfx, password);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxOutputPath))!);
            await File.WriteAllBytesAsync(pfxOutputPath, pfxBytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfxBytes);
        }

        string newCredentialJson = CreateKeyCredentialJson(keyId, certificate, startsOnUtc, expiresOnUtc);
        await ReplaceActiveKeyCredentialAsync(
            accessToken,
            currentSettings.Tenant.ApplicationObjectId,
            newCredentialJson,
            currentSettings.ActiveCertificate?.KeyId,
            cancellationToken).ConfigureAwait(false);

        var metadata = new AutopilotCertificateMetadata
        {
            KeyId = keyId,
            Thumbprint = certificate.Thumbprint?.ToUpperInvariant(),
            DisplayName = AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName,
            ExpiresOnUtc = expiresOnUtc
        };

        return new AutopilotCertificateCreationResult
        {
            Settings = currentSettings with { ActiveCertificate = metadata },
            GeneratedPassword = password,
            Certificate = metadata
        };
    }

    /// <inheritdoc />
    public async Task<AutopilotHardwareHashUploadSettings> RetireActiveCertificateAsync(
        AutopilotHardwareHashUploadSettings currentSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        if (string.IsNullOrWhiteSpace(currentSettings.Tenant.ApplicationObjectId) ||
            string.IsNullOrWhiteSpace(currentSettings.ActiveCertificate?.KeyId))
        {
            return currentSettings with { ActiveCertificate = null };
        }

        TokenCredential credential = CreateCredential();
        string accessToken = await AcquireAccessTokenAsync(credential, cancellationToken).ConfigureAwait(false);
        await RemoveKeyCredentialAsync(
            accessToken,
            currentSettings.Tenant.ApplicationObjectId,
            currentSettings.ActiveCertificate.KeyId,
            cancellationToken).ConfigureAwait(false);
        return currentSettings with { ActiveCertificate = null };
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

    private static async Task<string> GetTenantIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            OrganizationRequestPath,
            null,
            cancellationToken).ConfigureAwait(false);
        JsonElement organization = document.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
        return organization.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("Microsoft Graph did not return a tenant ID.");
    }

    private static async Task<GraphAppRole> GetGraphAppRoleAsync(
        string accessToken,
        string roleValue,
        CancellationToken cancellationToken)
    {
        string requestPath = $"v1.0/servicePrincipals?$filter=appId eq '{GraphAppId}'&$select=id,appRoles";
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            requestPath,
            null,
            cancellationToken).ConfigureAwait(false);
        JsonElement graphServicePrincipal = document.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
        string graphServicePrincipalId = graphServicePrincipal.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Microsoft Graph service principal ID was not returned.");

        foreach (JsonElement appRole in graphServicePrincipal.GetProperty("appRoles").EnumerateArray())
        {
            if (appRole.TryGetProperty("value", out JsonElement value) &&
                string.Equals(value.GetString(), roleValue, StringComparison.OrdinalIgnoreCase) &&
                appRole.TryGetProperty("id", out JsonElement id))
            {
                return new GraphAppRole(graphServicePrincipalId, id.GetString()!, roleValue);
            }
        }

        throw new InvalidOperationException($"Microsoft Graph app role '{roleValue}' was not found.");
    }

    private static async Task<AutopilotGraphApplication?> FindApplicationAsync(
        string accessToken,
        string? applicationObjectId,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(applicationObjectId))
        {
            return null;
        }

        try
        {
            using JsonDocument document = await SendGraphRequestAsync(
                accessToken,
                HttpMethod.Get,
                $"v1.0/applications/{applicationObjectId}?{ApplicationSelect}",
                null,
                cancellationToken).ConfigureAwait(false);
            return ParseApplication(document.RootElement, requiredRole);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<AutopilotGraphApplication?> FindApplicationByDisplayNameAsync(
        string accessToken,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        string displayName = AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName.Replace("'", "''", StringComparison.Ordinal);
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            $"v1.0/applications?$filter=displayName eq '{displayName}'&{ApplicationSelect}",
            null,
            cancellationToken).ConfigureAwait(false);
        JsonElement application = document.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
        return application.ValueKind == JsonValueKind.Undefined ? null : ParseApplication(application, requiredRole);
    }

    private static async Task<AutopilotGraphApplication> CreateApplicationAsync(
        string accessToken,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        string body = $$"""
            {
              "displayName": "{{AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName}}",
              "signInAudience": "AzureADMyOrg",
              "requiredResourceAccess": [
                {
                  "resourceAppId": "{{GraphAppId}}",
                  "resourceAccess": [
                    {
                      "id": "{{requiredRole.AppRoleId}}",
                      "type": "Role"
                    }
                  ]
                }
              ]
            }
            """;

        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Post,
            "v1.0/applications",
            body,
            cancellationToken).ConfigureAwait(false);
        return ParseApplication(document.RootElement, requiredRole);
    }

    private static async Task<AutopilotGraphApplication> EnsureRequiredApplicationPermissionAsync(
        string accessToken,
        AutopilotGraphApplication application,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        if (application.RequiredPermissionValues.Contains(requiredRole.Value))
        {
            return application;
        }

        string body = await CreateRequiredResourceAccessPatchBodyAsync(
            accessToken,
            application.ObjectId,
            requiredRole,
            cancellationToken).ConfigureAwait(false);
        try
        {
            await SendGraphNoContentAsync(
                accessToken,
                HttpMethod.Patch,
                $"v1.0/applications/{application.ObjectId}",
                body,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return application;
        }

        return application with
        {
            RequiredPermissionValues = AutopilotGraphPermissionCatalog.RequiredWinPeApplicationPermissionValues
        };
    }

    private static async Task<string> CreateRequiredResourceAccessPatchBodyAsync(
        string accessToken,
        string applicationObjectId,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            $"v1.0/applications/{applicationObjectId}?$select=requiredResourceAccess",
            null,
            cancellationToken).ConfigureAwait(false);

        List<string> resources = [];
        bool graphResourceFound = false;
        if (document.RootElement.TryGetProperty("requiredResourceAccess", out JsonElement requiredResourceAccess))
        {
            foreach (JsonElement resource in requiredResourceAccess.EnumerateArray())
            {
                string? resourceAppId = resource.TryGetProperty("resourceAppId", out JsonElement resourceAppIdElement)
                    ? resourceAppIdElement.GetString()
                    : null;
                if (!string.Equals(resourceAppId, GraphAppId, StringComparison.OrdinalIgnoreCase))
                {
                    resources.Add(resource.GetRawText());
                    continue;
                }

                graphResourceFound = true;
                string resourceAccessJson = CreateResourceAccessJson(resource, requiredRole);
                resources.Add($$"""
                    {
                      "resourceAppId": "{{GraphAppId}}",
                      "resourceAccess": [
                        {{resourceAccessJson}}
                      ]
                    }
                    """);
            }
        }

        if (!graphResourceFound)
        {
            resources.Add(CreateRequiredGraphResourceAccessJson(requiredRole));
        }

        return $$"""
            {
              "requiredResourceAccess": [
                {{string.Join(",", resources)}}
              ]
            }
            """;
    }

    private static string CreateResourceAccessJson(JsonElement graphResource, GraphAppRole requiredRole)
    {
        List<string> resourceAccess = graphResource.TryGetProperty("resourceAccess", out JsonElement resourceAccessElement)
            ? resourceAccessElement.EnumerateArray().Select(access => access.GetRawText()).ToList()
            : [];
        bool roleExists = resourceAccessElement.ValueKind == JsonValueKind.Array &&
                          resourceAccessElement.EnumerateArray().Any(access =>
                              access.TryGetProperty("id", out JsonElement id) &&
                              string.Equals(id.GetString(), requiredRole.AppRoleId, StringComparison.OrdinalIgnoreCase));
        if (!roleExists)
        {
            resourceAccess.Add(CreateRequiredResourceAccessEntryJson(requiredRole));
        }

        return string.Join(",", resourceAccess);
    }

    private static string CreateRequiredGraphResourceAccessJson(GraphAppRole requiredRole)
    {
        string resourceAccessJson = CreateRequiredResourceAccessEntryJson(requiredRole);
        return $$"""
            {
              "resourceAppId": "{{GraphAppId}}",
              "resourceAccess": [
                {{resourceAccessJson}}
              ]
            }
            """;
    }

    private static string CreateRequiredResourceAccessEntryJson(GraphAppRole requiredRole)
    {
        return $$"""
            {
              "id": "{{requiredRole.AppRoleId}}",
              "type": "Role"
            }
            """;
    }

    private static async Task<AutopilotGraphServicePrincipal?> FindServicePrincipalAsync(
        string accessToken,
        string clientId,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            $"v1.0/servicePrincipals?$filter=appId eq '{clientId}'&{ServicePrincipalSelect}",
            null,
            cancellationToken).ConfigureAwait(false);
        JsonElement servicePrincipal = document.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();
        if (servicePrincipal.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return await ParseServicePrincipalAsync(accessToken, servicePrincipal, requiredRole, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AutopilotGraphServicePrincipal> CreateServicePrincipalAsync(
        string accessToken,
        string clientId,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        string body = $$"""
            {
              "appId": "{{clientId}}"
            }
            """;
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Post,
            "v1.0/servicePrincipals",
            body,
            cancellationToken).ConfigureAwait(false);
        return await ParseServicePrincipalAsync(accessToken, document.RootElement, requiredRole, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AutopilotGraphServicePrincipal> EnsureAdminConsentAsync(
        string accessToken,
        AutopilotGraphServicePrincipal servicePrincipal,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        if (servicePrincipal.ConsentedPermissionValues.Contains(requiredRole.Value))
        {
            return servicePrincipal;
        }

        string body = $$"""
            {
              "principalId": "{{servicePrincipal.ObjectId}}",
              "resourceId": "{{requiredRole.ResourceServicePrincipalId}}",
              "appRoleId": "{{requiredRole.AppRoleId}}"
            }
            """;
        try
        {
            await SendGraphNoContentAsync(
                accessToken,
                HttpMethod.Post,
                $"v1.0/servicePrincipals/{servicePrincipal.ObjectId}/appRoleAssignments",
                body,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return servicePrincipal;
        }

        return servicePrincipal with
        {
            ConsentedPermissionValues = AutopilotGraphPermissionCatalog.RequiredWinPeApplicationPermissionValues
        };
    }

    private static async Task<IReadOnlyList<AutopilotGraphKeyCredential>> GetApplicationKeyCredentialsAsync(
        string accessToken,
        string applicationObjectId,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            $"v1.0/applications/{applicationObjectId}?$select=keyCredentials",
            null,
            cancellationToken).ConfigureAwait(false);
        return ParseKeyCredentials(document.RootElement);
    }

    private static async Task ReplaceActiveKeyCredentialAsync(
        string accessToken,
        string applicationObjectId,
        string newCredentialJson,
        string? activeKeyIdToReplace,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            $"v1.0/applications/{applicationObjectId}?$select=keyCredentials",
            null,
            cancellationToken).ConfigureAwait(false);

        string existingCredentialsJson = document.RootElement.TryGetProperty("keyCredentials", out JsonElement keyCredentials)
            ? string.Join(
                ",",
                keyCredentials.EnumerateArray().Where(credential =>
                    string.IsNullOrWhiteSpace(activeKeyIdToReplace) ||
                    !credential.TryGetProperty("keyId", out JsonElement existingKeyId) ||
                    !string.Equals(existingKeyId.GetString(), activeKeyIdToReplace, StringComparison.OrdinalIgnoreCase))
                    .Select(credential => credential.GetRawText()))
            : string.Empty;
        string separator = string.IsNullOrWhiteSpace(existingCredentialsJson) ? string.Empty : ",";
        string body = $$"""
            {
              "keyCredentials": [
                {{existingCredentialsJson}}{{separator}}{{newCredentialJson}}
              ]
            }
            """;

        await SendGraphNoContentAsync(
            accessToken,
            HttpMethod.Patch,
            $"v1.0/applications/{applicationObjectId}",
            body,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RemoveKeyCredentialAsync(
        string accessToken,
        string applicationObjectId,
        string keyId,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            $"v1.0/applications/{applicationObjectId}?$select=keyCredentials",
            null,
            cancellationToken).ConfigureAwait(false);

        string retainedCredentialsJson = document.RootElement.TryGetProperty("keyCredentials", out JsonElement keyCredentials)
            ? string.Join(
                ",",
                keyCredentials.EnumerateArray().Where(credential =>
                    !credential.TryGetProperty("keyId", out JsonElement existingKeyId) ||
                    !string.Equals(existingKeyId.GetString(), keyId, StringComparison.OrdinalIgnoreCase))
                    .Select(credential => credential.GetRawText()))
            : string.Empty;
        string body = $$"""
            {
              "keyCredentials": [
                {{retainedCredentialsJson}}
              ]
            }
            """;

        await SendGraphNoContentAsync(
            accessToken,
            HttpMethod.Patch,
            $"v1.0/applications/{applicationObjectId}",
            body,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string[]> GetGroupTagsAsync(string accessToken, CancellationToken cancellationToken)
    {
        List<string> groupTags = [];
        string? requestPath = GroupTagRequestPath;
        while (!string.IsNullOrWhiteSpace(requestPath))
        {
            using JsonDocument document = await SendGraphRequestAsync(
                accessToken,
                HttpMethod.Get,
                requestPath,
                null,
                cancellationToken).ConfigureAwait(false);

            foreach (JsonElement item in document.RootElement.GetProperty("value").EnumerateArray())
            {
                if (item.TryGetProperty("groupTag", out JsonElement groupTag) &&
                    groupTag.GetString()?.Trim() is { Length: > 0 } value)
                {
                    groupTags.Add(value);
                }
            }

            requestPath = document.RootElement.TryGetProperty("@odata.nextLink", out JsonElement nextLink)
                ? nextLink.GetString()
                : null;
        }

        return groupTags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(groupTag => groupTag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<JsonDocument> SendGraphRequestAsync(
        string accessToken,
        HttpMethod method,
        string requestPath,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendGraphRawAsync(
            accessToken,
            method,
            requestPath,
            jsonBody,
            cancellationToken).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(responseBody);
    }

    private static async Task SendGraphNoContentAsync(
        string accessToken,
        HttpMethod method,
        string requestPath,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendGraphRawAsync(
            accessToken,
            method,
            requestPath,
            jsonBody,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendGraphRawAsync(
        string accessToken,
        HttpMethod method,
        string requestPath,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response = await GraphHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            string message = $"Microsoft Graph request failed for '{method} {requestPath}' with status code {(int)response.StatusCode}.";
            throw new HttpRequestException(message, null, response.StatusCode);
        }

        return response;
    }

    private static AutopilotGraphApplication ParseApplication(JsonElement application, GraphAppRole requiredRole)
    {
        string objectId = application.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Application object ID was not returned.");
        string clientId = application.GetProperty("appId").GetString()
            ?? throw new InvalidOperationException("Application client ID was not returned.");
        string displayName = application.GetProperty("displayName").GetString() ?? string.Empty;
        HashSet<string> requiredPermissionValues = new(StringComparer.OrdinalIgnoreCase);

        if (application.TryGetProperty("requiredResourceAccess", out JsonElement requiredResourceAccess))
        {
            foreach (JsonElement resourceAccess in requiredResourceAccess.EnumerateArray())
            {
                if (resourceAccess.TryGetProperty("resourceAccess", out JsonElement permissions))
                {
                    foreach (JsonElement permission in permissions.EnumerateArray())
                    {
                        if (permission.TryGetProperty("id", out JsonElement id) &&
                            string.Equals(id.GetString(), requiredRole.AppRoleId, StringComparison.OrdinalIgnoreCase))
                        {
                            requiredPermissionValues.Add(requiredRole.Value);
                        }
                    }
                }
            }
        }

        return new AutopilotGraphApplication(objectId, clientId, displayName, requiredPermissionValues);
    }

    private static async Task<AutopilotGraphServicePrincipal> ParseServicePrincipalAsync(
        string accessToken,
        JsonElement servicePrincipal,
        GraphAppRole requiredRole,
        CancellationToken cancellationToken)
    {
        string objectId = servicePrincipal.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Service principal object ID was not returned.");
        bool isEnabled = !servicePrincipal.TryGetProperty("accountEnabled", out JsonElement accountEnabled) ||
                         accountEnabled.GetBoolean();
        HashSet<string> consentedPermissionValues = new(StringComparer.OrdinalIgnoreCase);
        using JsonDocument assignments = await SendGraphRequestAsync(
            accessToken,
            HttpMethod.Get,
            $"v1.0/servicePrincipals/{objectId}/appRoleAssignments?$select=appRoleId",
            null,
            cancellationToken).ConfigureAwait(false);
        foreach (JsonElement assignment in assignments.RootElement.GetProperty("value").EnumerateArray())
        {
            if (assignment.TryGetProperty("appRoleId", out JsonElement appRoleId) &&
                string.Equals(appRoleId.GetString(), requiredRole.AppRoleId, StringComparison.OrdinalIgnoreCase))
            {
                consentedPermissionValues.Add(requiredRole.Value);
            }
        }

        return new AutopilotGraphServicePrincipal(objectId, isEnabled, consentedPermissionValues);
    }

    private static IReadOnlyList<AutopilotGraphKeyCredential> ParseKeyCredentials(JsonElement application)
    {
        if (!application.TryGetProperty("keyCredentials", out JsonElement keyCredentials))
        {
            return [];
        }

        List<AutopilotGraphKeyCredential> credentials = [];
        foreach (JsonElement credential in keyCredentials.EnumerateArray())
        {
            string keyId = credential.GetProperty("keyId").GetString() ?? string.Empty;
            string displayName = credential.TryGetProperty("displayName", out JsonElement displayNameElement)
                ? displayNameElement.GetString() ?? string.Empty
                : string.Empty;
            string thumbprint = credential.TryGetProperty("customKeyIdentifier", out JsonElement customKeyIdentifier) &&
                                customKeyIdentifier.GetString() is { Length: > 0 } base64Thumbprint
                ? Convert.ToHexString(Convert.FromBase64String(base64Thumbprint))
                : string.Empty;
            DateTimeOffset startsOnUtc = credential.TryGetProperty("startDateTime", out JsonElement startDateTime) &&
                                         startDateTime.GetDateTimeOffset() is { } start
                ? start
                : DateTimeOffset.MinValue;
            DateTimeOffset expiresOnUtc = credential.TryGetProperty("endDateTime", out JsonElement endDateTime) &&
                                          endDateTime.GetDateTimeOffset() is { } end
                ? end
                : DateTimeOffset.MinValue;
            credentials.Add(new AutopilotGraphKeyCredential(keyId, displayName, thumbprint, startsOnUtc, expiresOnUtc));
        }

        return credentials;
    }

    private static string? ResolveDefaultGroupTag(string? currentDefaultGroupTag, string[] groupTags)
    {
        if (!string.IsNullOrWhiteSpace(currentDefaultGroupTag))
        {
            return currentDefaultGroupTag.Trim();
        }

        return groupTags.FirstOrDefault();
    }

    private static string CreateKeyCredentialJson(
        string keyId,
        X509Certificate2 certificate,
        DateTimeOffset startsOnUtc,
        DateTimeOffset expiresOnUtc)
    {
        string certificateRawData = Convert.ToBase64String(certificate.RawData);
        string customKeyIdentifier = Convert.ToBase64String(certificate.GetCertHash());
        return $$"""
            {
              "customKeyIdentifier": "{{customKeyIdentifier}}",
              "displayName": "{{AutopilotHardwareHashUploadSettings.ManagedAppRegistrationDisplayName}}",
              "endDateTime": "{{expiresOnUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}}",
              "key": "{{certificateRawData}}",
              "keyId": "{{keyId}}",
              "startDateTime": "{{startsOnUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}}",
              "type": "AsymmetricX509Cert",
              "usage": "Verify"
            }
            """;
    }

    private static string GeneratePfxPassword()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record GraphAppRole(
        string ResourceServicePrincipalId,
        string AppRoleId,
        string Value);
}
