// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Autopilot;

/// <summary>
/// OAuth 2.0 device code flow constants used by <c>Foundry.AutopilotInteractive</c> to sign the technician in against
/// Microsoft Entra ID.
/// </summary>
/// <remarks>
/// The client is a public client: none of these values is a secret. <c>openid profile</c> adds the id_token used only to
/// display the signed-in account; the Graph scope authorizes the Autopilot device import.
/// </remarks>
public static class AutopilotInteractiveAuthentication
{
    /// <summary>Tenant segment of the authority; <c>common</c> lets the technician sign in with any work account.</summary>
    public const string Tenant = "common";

    /// <summary>Application (client) identifier of the Foundry public client.</summary>
    public const string ClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";

    /// <summary>Space-separated scopes requested at sign-in.</summary>
    public const string Scopes = "openid profile DeviceManagementServiceConfig.ReadWrite.All";

    /// <summary>Device code endpoint of the v2.0 authority.</summary>
    public const string DeviceCodeEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/devicecode";

    /// <summary>Token endpoint of the v2.0 authority.</summary>
    public const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
}
