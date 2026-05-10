using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

public interface IAutopilotTenantDownloadDialogService
{
    Task<IReadOnlyList<AutopilotProfileSettings>?> DownloadAsync(
        Func<CancellationToken, Task<IReadOnlyList<AutopilotProfileSettings>>> downloadProfilesAsync);
}
