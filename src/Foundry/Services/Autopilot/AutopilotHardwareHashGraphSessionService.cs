// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Azure.Core;
using Azure.Identity;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Owns the session-scoped interactive Microsoft Graph credential used for Autopilot hardware hash onboarding.
/// </summary>
public sealed class AutopilotHardwareHashGraphSessionService : IAutopilotHardwareHashGraphSessionService
{
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);

    private static readonly string[] GraphScopes =
    [
        "Application.ReadWrite.All",
        "AppRoleAssignment.ReadWrite.All",
        "DeviceManagementServiceConfig.Read.All",
        "User.Read"
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
}
