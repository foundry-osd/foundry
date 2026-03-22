using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Autopilot;

public interface IAutopilotProfileCatalogService
{
    IReadOnlyList<AutopilotProfileCatalogItem> LoadAvailableProfiles();
}
