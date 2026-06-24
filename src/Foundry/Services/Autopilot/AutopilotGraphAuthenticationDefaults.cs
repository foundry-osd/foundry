// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Autopilot;

/// <summary>
/// Defines the Foundry-owned Microsoft Graph public client used for interactive Autopilot tenant onboarding.
/// </summary>
internal static class AutopilotGraphAuthenticationDefaults
{
    /// <summary>
    /// Official multi-tenant public client ID for the Foundry bootstrap app.
    /// This app is used only for interactive OSD admin sign-in and is not embedded into generated WinPE media.
    /// </summary>
    public const string FoundryBootstrapClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";

    /// <summary>
    /// Optional override for private builds or forks that use their own bootstrap public client.
    /// </summary>
    public const string ClientIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_CLIENT_ID";

    /// <summary>
    /// Optional tenant override for development and controlled validation scenarios.
    /// </summary>
    public const string TenantIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_TENANT_ID";

    public const string DefaultTenantId = "common";
    public const string RedirectUri = "http://localhost";
}
