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
    Task<IReadOnlyList<AutopilotProfileSettings>> DownloadFromTenantAsync(CancellationToken cancellationToken = default);
}
