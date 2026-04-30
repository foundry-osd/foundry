namespace Foundry.Core.Models.Configuration;

public sealed record NetworkSettings
{
    public bool WifiProvisioned { get; init; }
    public Dot1xSettings Dot1x { get; init; } = new();
    public WifiSettings Wifi { get; init; } = new();
}
