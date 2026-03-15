namespace Foundry.Models.Configuration;

public sealed record WifiSettings
{
    public bool IsEnabled { get; init; }
    public string? Ssid { get; init; }
    public string? SecurityType { get; init; }
}
