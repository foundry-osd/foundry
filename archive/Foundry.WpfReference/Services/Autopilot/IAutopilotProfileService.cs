using Foundry.Models.Configuration;

namespace Foundry.Services.Autopilot;

public interface IAutopilotProfileService
{
    Task<AutopilotProfileSettings> ImportFromJsonFileAsync(string filePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AutopilotProfileSettings>> DownloadFromTenantAsync(CancellationToken cancellationToken = default);
}
