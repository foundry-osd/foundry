using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Hardware;

public interface IHardwareProfileService
{
    Task<HardwareProfile> GetCurrentAsync(CancellationToken cancellationToken = default);
}
