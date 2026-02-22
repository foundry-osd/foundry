using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Autopilot;

public interface IAutopilotService
{
    Task<AutopilotExecutionResult> ExecuteFullWorkflowAsync(
        string cacheRootPath,
        string windowsPartitionRoot,
        HardwareProfile hardwareProfile,
        OperatingSystemCatalogItem operatingSystem,
        CancellationToken cancellationToken = default);
}
