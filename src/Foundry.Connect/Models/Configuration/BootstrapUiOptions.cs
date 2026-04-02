namespace Foundry.Connect.Models.Configuration;

public sealed class BootstrapUiOptions
{
    public string WindowTitle { get; init; } = "Foundry.Connect";

    public int AutoCloseDelaySeconds { get; init; } = 5;

    public int RefreshIntervalSeconds { get; init; } = 5;
}
