using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Cache;

public interface ICacheLocatorService
{
    Task<CacheResolution> ResolveAsync(
        DeploymentMode mode,
        string? preferredRootPath = null,
        CancellationToken cancellationToken = default);
}
