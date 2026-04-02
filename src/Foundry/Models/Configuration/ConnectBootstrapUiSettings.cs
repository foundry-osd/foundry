namespace Foundry.Models.Configuration;

public sealed record ConnectBootstrapUiSettings
{
    public string WindowTitle { get; init; } = "Foundry.Connect";

    public int AutoCloseDelaySeconds { get; init; } = 5;

    public int RefreshIntervalSeconds { get; init; } = 5;
}
