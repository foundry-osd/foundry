// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Autopilot;

/// <summary>
/// The Foundry-owned multi-tenant Microsoft Entra public client that signs in to Microsoft Graph for Autopilot.
/// </summary>
/// <remarks>
/// Foundry OSD uses it for the interactive administrator sign-in of tenant onboarding and hardware hash upload, and
/// <c>Foundry.AutopilotInteractive</c> uses it for the technician device code sign-in in the target OS. A public client has no
/// secret, so the identifier ships in the <c>Foundry.AutopilotInteractive</c> runtime that Deploy installs from boot media.
/// </remarks>
public static class FoundryGraphPublicClient
{
    /// <summary>Application (client) identifier of the Foundry public client.</summary>
    public const string ClientId = "83eb3a92-030d-49b7-881b-32a1eb3e110a";
}
