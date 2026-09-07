// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Downloads Windows Autopilot deployment profiles from the signed-in tenant.
/// </summary>
public interface IAutopilotTenantProfileService
{
    /// <summary>
    /// Opens interactive Microsoft Graph sign-in when needed and downloads available Autopilot profiles.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels token acquisition and Graph download operations.</param>
    /// <returns>The profiles converted to Foundry configuration settings.</returns>
    Task<AutopilotProfileDownloadResult> DownloadFromTenantAsync(CancellationToken cancellationToken = default);
}

/// <summary>Separates usable offline profiles from explicit per-profile rejection diagnostics.</summary>
public sealed record AutopilotProfileDownloadResult(IReadOnlyList<AutopilotProfileSettings> SupportedProfiles,
    IReadOnlyList<AutopilotProfileRejection> RejectedProfiles);

/// <summary>Contains only the user-visible profile name and a sanitized domain-validation reason.</summary>
public sealed record AutopilotProfileRejection(string DisplayName, string Reason);
