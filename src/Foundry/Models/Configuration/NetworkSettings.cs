namespace Foundry.Models.Configuration;

public sealed record NetworkSettings
{
    public Dot1xSettings Dot1x { get; init; } = new();
    public WifiSettings Wifi { get; init; } = new();
}
