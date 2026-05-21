using Azure.Core;
using Azure.Identity;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Owns the session-scoped interactive Microsoft Graph credential used for Autopilot hardware hash onboarding.
/// </summary>
public sealed class AutopilotHardwareHashGraphSessionService : IAutopilotHardwareHashGraphSessionService
{
    private const string DefaultClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
    private const string ClientIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_CLIENT_ID";
    private const string TenantIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_TENANT_ID";
    private const string DefaultTenantId = "common";
    private const string DefaultRedirectUri = "http://localhost";
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);

    private static readonly string[] GraphScopes =
    [
        "Application.ReadWrite.All",
        "AppRoleAssignment.ReadWrite.All",
        "DeviceManagementServiceConfig.Read.All"
    ];

    private TokenCredential? credential;
    private AccessToken? currentToken;

    /// <inheritdoc />
    public async Task<string> ConnectAsync(CancellationToken cancellationToken = default)
    {
        credential = CreateCredential();
        try
        {
            currentToken = await credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken)
                .ConfigureAwait(false);
            return currentToken.Value.Token;
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (currentToken is { } cachedToken &&
            cachedToken.ExpiresOn > DateTimeOffset.UtcNow.Add(TokenRefreshSkew))
        {
            return cachedToken.Token;
        }

        if (credential is null)
        {
            throw new InvalidOperationException("Connect to the tenant before managing Autopilot certificates.");
        }

        currentToken = await credential.GetTokenAsync(new TokenRequestContext(GraphScopes), cancellationToken)
            .ConfigureAwait(false);
        return currentToken.Value.Token;
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        credential = null;
        currentToken = null;
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
}
