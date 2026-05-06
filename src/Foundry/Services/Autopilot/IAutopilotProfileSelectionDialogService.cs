using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Autopilot;

public interface IAutopilotProfileSelectionDialogService
{
    Task<IReadOnlyList<AutopilotProfileSettings>?> PickProfilesAsync(
        IReadOnlyList<AutopilotProfileSettings> availableProfiles);
}
