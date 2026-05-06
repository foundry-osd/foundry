using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

public interface IAutopilotTenantProfileService
{
    Task<IReadOnlyList<AutopilotProfileSettings>> DownloadFromTenantAsync(CancellationToken cancellationToken = default);
}
