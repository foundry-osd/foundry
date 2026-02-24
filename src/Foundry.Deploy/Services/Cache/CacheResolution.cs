using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Cache;

public sealed record CacheResolution
{
    public required DeploymentMode Mode { get; init; }
    public required string RootPath { get; init; }
    public required string Source { get; init; }
    public required bool IsPersistent { get; init; }
}
