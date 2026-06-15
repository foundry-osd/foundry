namespace Foundry.Core.Services.WinPe.OsRecovery;

public sealed record OsRecoveryBootMenuLocalization
{
    public string Culture { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
