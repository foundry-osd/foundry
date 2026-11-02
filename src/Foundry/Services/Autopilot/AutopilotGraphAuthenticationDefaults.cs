// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Autopilot;

/// <summary>
/// Defines how Foundry OSD signs in to Microsoft Graph for interactive Autopilot tenant onboarding and hardware hash upload.
/// The default client is <see cref="Foundry.Core.Services.Autopilot.FoundryGraphPublicClient"/>.
/// </summary>
internal static class AutopilotGraphAuthenticationDefaults
{
    /// <summary>
    /// Optional override for private builds or forks that use their own public client. It applies only to the Foundry OSD
    /// sign-in: the <c>Foundry.AutopilotInteractive</c> runtime always uses the built-in client.
    /// </summary>
    public const string ClientIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_CLIENT_ID";

    /// <summary>
    /// Optional tenant override for development and controlled validation scenarios.
    /// </summary>
    public const string TenantIdEnvironmentVariableName = "FOUNDRY_AUTOPILOT_GRAPH_TENANT_ID";

    public const string DefaultTenantId = "common";
    public const string RedirectUri = "http://localhost";
}
