namespace Foundry.Deploy.Services.Cache;

public sealed record CacheResolution
{
    public required string RootPath { get; init; }
    public required string Source { get; init; }
}
