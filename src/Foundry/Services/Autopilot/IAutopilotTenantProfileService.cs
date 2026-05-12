using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

/// <summary>
/// Downloads Windows Autopilot deployment profiles from the signed-in tenant.
/// </summary>
public interface IAutopilotTenantProfileService
{
    /// <summary>
    /// Authenticates to Microsoft Graph and downloads available Autopilot profiles.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels tenant access and download operations.</param>
    /// <returns>The profiles converted to Foundry configuration settings.</returns>
    Task<IReadOnlyList<AutopilotProfileSettings>> DownloadFromTenantAsync(CancellationToken cancellationToken = default);
}
